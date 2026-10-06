using API_Thanh_toan.Data;
using API_Thanh_toan.Models;
using Microsoft.Extensions.Caching.Memory;

namespace API_Thanh_toan.Services;

public class PaymentService : IPaymentService
{
    private readonly IPaymentRepository _paymentRepository;
    private readonly IInvoiceRepository _invoiceRepository;
    private readonly IMemoryCache _cache;
    private readonly ILogger<PaymentService> _logger;
    private readonly int _rateLimitThreshold;

    public PaymentService(
        IPaymentRepository paymentRepository,
        IInvoiceRepository invoiceRepository,
        IMemoryCache cache,
        Microsoft.Extensions.Configuration.IConfiguration configuration,
        ILogger<PaymentService> logger)
    {
        _paymentRepository = paymentRepository;
        _invoiceRepository = invoiceRepository;
        _cache = cache;
        _logger = logger;
        _rateLimitThreshold = int.TryParse(configuration["SecuritySettings:PaymentRateLimit"], out var val) ? val : 20;
    }

    public bool IsRateLimited(string clientId)
    {
        if (string.IsNullOrWhiteSpace(clientId)) return false;

        // Fixed-window rate limiting per minute
        var currentMinute = DateTime.UtcNow.ToString("yyyyMMddHHmm");
        var key = $"rate-limit:payment:{clientId.ToLowerInvariant()}:{currentMinute}";

        if (_cache.TryGetValue<int>(key, out var count))
        {
            if (count >= _rateLimitThreshold)
            {
                return true;
            }
            _cache.Set(key, count + 1, TimeSpan.FromMinutes(2));
        }
        else
        {
            _cache.Set(key, 1, TimeSpan.FromMinutes(2));
        }

        return false;
    }

    public async Task<(int StatusCode, PaymentResponse? Response, string? SystemError)> ProcessPaymentAsync(string clientId, PaymentRequest request)
    {
        // Step 2: Check Client payment authorization (MANH IS NOT NULL)
        var clientDetails = await _paymentRepository.GetClientAuthDetailsAsync(clientId);
        if (clientDetails == null || string.IsNullOrWhiteSpace(clientDetails.Manh))
        {
            _logger.LogWarning("Payment rejected: Client '{ClientId}' is not authorized for payment processing.", clientId);
            return (StatusCodes.Status403Forbidden, null, "not_authorized_for_payment");
        }

        var clientManh = clientDetails.Manh;

        if (!clientDetails.PayId.HasValue)
        {
            _logger.LogWarning("Payment rejected: Client '{ClientId}' does not have a configured payID.", clientId);
            return (StatusCodes.Status403Forbidden, null, "pay_user_not_found");
        }

        // Step 3: Phase A Insert & Idempotency check
        var (txId, phaseAResult, existing) = await _paymentRepository.InsertProcessingTransactionAsync(
            clientId,
            request.PartnerTransactionId,
            request.GatewayTransactionId,
            request.CustomerCode!.Value,
            request.TotalAmount!.Value,
            request.PaymentTime ?? DateTime.UtcNow,
            request.PaymentProvider,
            request.PaymentChannel
        );

        if (phaseAResult == "ALREADY_COMPLETED" && existing != null)
        {
            _logger.LogInformation("Idempotent replay: PartnerTransactionId '{PartnerTxId}' for Client '{ClientId}' already completed with status '{Status}'.",
                request.PartnerTransactionId, clientId, existing.Status);

            var existingReceiptCode = existing.Status == "SUCCESS" ? $"PMT-{existing.Id:D9}" : null;
            return (StatusCodes.Status200OK, new PaymentResponse
            {
                Status = existing.Status,
                ReceiptCode = existingReceiptCode,
                ProcessedAt = existing.ProcessedAt,
                ErrorCode = existing.ErrorCode,
                ErrorMessage = existing.ErrorMessage
            }, null);
        }

        if (phaseAResult == "PROCESSING_IN_PROGRESS")
        {
            _logger.LogWarning("Idempotent rejection: PartnerTransactionId '{PartnerTxId}' for Client '{ClientId}' is currently processing.",
                request.PartnerTransactionId, clientId);
            return (StatusCodes.Status409Conflict, null, "processing_in_progress");
        }

        var receiptCode = $"PMT-{txId:D9}";

        // Step 5 Check: Invoices array not empty & no duplicate invoiceCodes in request
        if (request.Invoices == null || request.Invoices.Count == 0)
        {
            await _paymentRepository.UpdateTransactionStatusAsync(txId, "FAILED", "invalid_request", "Danh sách hóa đơn thanh toán rỗng.");
            return (StatusCodes.Status200OK, new PaymentResponse
            {
                Status = "FAILED",
                ReceiptCode = null,
                ErrorCode = "invalid_request",
                ErrorMessage = "Danh sách hóa đơn thanh toán rỗng."
            }, null);
        }

        var duplicateCode = request.Invoices
            .Where(x => x.InvoiceCode.HasValue)
            .GroupBy(x => x.InvoiceCode!.Value)
            .FirstOrDefault(g => g.Count() > 1);

        if (duplicateCode != null)
        {
            var msg = $"Danh sách hóa đơn chứa mã hóa đơn trùng lặp: {duplicateCode.Key}.";
            await _paymentRepository.UpdateTransactionStatusAsync(txId, "FAILED", "duplicate_invoice_in_request", msg);
            return (StatusCodes.Status200OK, new PaymentResponse
            {
                Status = "FAILED",
                ReceiptCode = null,
                ErrorCode = "duplicate_invoice_in_request",
                ErrorMessage = msg
            }, null);
        }

        // Step 4: Customer eligibility check
        var customer = await _invoiceRepository.GetCustomerByIdAsync(request.CustomerCode.Value);
        if (customer == null || customer.Ttsd == "CUP" || customer.GcdbKdpt)
        {
            var msg = $"Khách hàng {request.CustomerCode.Value} không tồn tại hoặc không đủ điều kiện thu tiền.";
            await _paymentRepository.UpdateTransactionStatusAsync(txId, "FAILED", "customer_not_eligible", msg);
            return (StatusCodes.Status200OK, new PaymentResponse
            {
                Status = "FAILED",
                ReceiptCode = null,
                ErrorCode = "customer_not_eligible",
                ErrorMessage = msg
            }, null);
        }

        // Step 6: Total Amount check
        var sumApplied = request.Invoices.Sum(x => x.AmountApplied ?? 0);
        if (request.TotalAmount.Value != sumApplied)
        {
            var msg = $"Tổng số tiền thanh toán ({request.TotalAmount.Value}) không khớp với tổng số tiền của các hóa đơn ({sumApplied}).";
            await _paymentRepository.UpdateTransactionStatusAsync(txId, "FAILED", "amount_mismatch", msg);
            return (StatusCodes.Status200OK, new PaymentResponse
            {
                Status = "FAILED",
                ReceiptCode = null,
                ErrorCode = "amount_mismatch",
                ErrorMessage = msg
            }, null);
        }

        // Step 6.5: Chronological Payment Ordering Rule check (prior_invoices_unpaid)
        var dbUnpaidInvoices = (await _invoiceRepository.GetUnpaidInvoicesByCustomerIdAsync(request.CustomerCode.Value))
            .OrderBy(x => x.InvoiceYear)
            .ThenBy(x => x.InvoiceMonth)
            .ToList();

        if (dbUnpaidInvoices.Count > 0)
        {
            var requestInvoiceCodes = request.Invoices
                .Where(x => x.InvoiceCode.HasValue)
                .Select(x => x.InvoiceCode!.Value)
                .ToHashSet();

            var expectedCount = requestInvoiceCodes.Count;
            var expectedInvoiceCodes = dbUnpaidInvoices.Take(expectedCount).Select(x => x.InvoiceCode).ToHashSet();

            bool isChronologicalValid = expectedInvoiceCodes.Count == expectedCount &&
                expectedInvoiceCodes.SetEquals(requestInvoiceCodes);

            if (!isChronologicalValid)
            {
                var msg = "Khách hàng còn hóa đơn thuộc kỳ cũ hơn chưa được thanh toán.";
                await _paymentRepository.UpdateTransactionStatusAsync(txId, "FAILED", "prior_invoices_unpaid", msg);
                return (StatusCodes.Status200OK, new PaymentResponse
                {
                    Status = "FAILED",
                    ReceiptCode = null,
                    ErrorCode = "prior_invoices_unpaid",
                    ErrorMessage = msg
                }, null);
            }
        }

        // Step 7: Phase B Execution inside Dapper IDbTransaction
        var (success, errorCode, errorMessage) = await _paymentRepository.ExecutePaymentPhaseBAsync(
            txId,
            clientDetails.PayId.Value,
            clientManh,
            request.CustomerCode.Value,
            request.PayerAccountHolder,
            request.PayerAccountNumber,
            request.Invoices
        );

        if (!success)
        {
            await _paymentRepository.UpdateTransactionStatusAsync(txId, "FAILED", errorCode, errorMessage);
            return (StatusCodes.Status200OK, new PaymentResponse
            {
                Status = "FAILED",
                ReceiptCode = null,
                ErrorCode = errorCode,
                ErrorMessage = errorMessage
            }, null);
        }

        // Everything succeeded -> Update status to SUCCESS and return receiptCode
        await _paymentRepository.UpdateTransactionStatusAsync(txId, "SUCCESS", null, null);
        
        _logger.LogInformation("Payment successful: Client '{ClientId}' processed Receipt '{ReceiptCode}' for Customer '{CustomerCode}'.",
            clientId, receiptCode, request.CustomerCode.Value);

        return (StatusCodes.Status200OK, new PaymentResponse
        {
            Status = "SUCCESS",
            ReceiptCode = receiptCode,
            ProcessedAt = DateTime.UtcNow,
            ErrorCode = null,
            ErrorMessage = null
        }, null);
    }
}
