using API_Thanh_toan.Data;
using API_Thanh_toan.Models;
using Microsoft.Extensions.Caching.Memory;

namespace API_Thanh_toan.Services;

public class InvoiceValidationService : IInvoiceValidationService
{
    private readonly IInvoiceValidationRepository _repository;
    private readonly IMemoryCache _cache;
    private readonly int _rateLimitThreshold;

    public InvoiceValidationService(IInvoiceValidationRepository repository, IMemoryCache cache, Microsoft.Extensions.Configuration.IConfiguration configuration)
    {
        _repository = repository;
        _cache = cache;
        _rateLimitThreshold = int.TryParse(configuration["SecuritySettings:InvoiceValidationRateLimit"], out var val) ? val : 30;
    }

    public bool IsRateLimited(string clientId)
    {
        if (string.IsNullOrWhiteSpace(clientId)) return false;

        // Fixed-window rate limiting per minute
        var currentMinute = DateTime.UtcNow.ToString("yyyyMMddHHmm");
        var key = $"rate-limit:validate:{clientId.ToLowerInvariant()}:{currentMinute}";

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

    public async Task<ValidateInvoiceResponse> ValidateInvoiceAsync(int customerCode, int invoiceCode)
    {
        // 1. Fetch data from DB via cross-check single query
        var result = await _repository.ValidateInvoiceCrossCheckAsync(customerCode, invoiceCode);

        // 2. Perform sequential checks
        if (result == null)
        {
            return new ValidateInvoiceResponse
            {
                IsValid = false,
                InvoiceStatus = null,
                AmountDue = null,
                Reason = "invoice_not_found"
            };
        }

        // Rule 1: Customer eligibility
        if (result.Ttsd == "CUP" || result.GcdbKdpt)
        {
            return new ValidateInvoiceResponse
            {
                IsValid = false,
                InvoiceStatus = null,
                AmountDue = null,
                Reason = "customer_not_eligible"
            };
        }

        // Rule 2: Payment status
        if (!result.Conno || !string.IsNullOrWhiteSpace(result.CashManh))
        {
            return new ValidateInvoiceResponse
            {
                IsValid = false,
                InvoiceStatus = null,
                AmountDue = null,
                Reason = "already_paid"
            };
        }

        // Rule 3: Electronic Invoice release status
        if (result.EState != "DONE" && result.EState != "CASTED")
        {
            return new ValidateInvoiceResponse
            {
                IsValid = false,
                InvoiceStatus = null,
                AmountDue = null,
                Reason = "invoice_not_issued"
            };
        }

        // If all checks pass
        return new ValidateInvoiceResponse
        {
            IsValid = true,
            InvoiceStatus = "UNPAID",
            AmountDue = result.Tongcong,
            Reason = null
        };
    }
}
