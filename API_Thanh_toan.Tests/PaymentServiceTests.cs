using API_Thanh_toan.Data;
using API_Thanh_toan.Models;
using API_Thanh_toan.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace API_Thanh_toan.Tests;

public class PaymentServiceTests
{
    private readonly Mock<IPaymentRepository> _paymentRepositoryMock;
    private readonly Mock<IInvoiceRepository> _invoiceRepositoryMock;
    private readonly IMemoryCache _cache;
    private readonly Mock<IConfiguration> _configMock;
    private readonly Mock<ILogger<PaymentService>> _loggerMock;
    private readonly PaymentService _paymentService;

    public PaymentServiceTests()
    {
        _paymentRepositoryMock = new Mock<IPaymentRepository>();
        _invoiceRepositoryMock = new Mock<IInvoiceRepository>();
        _cache = new MemoryCache(new MemoryCacheOptions());
        _configMock = new Mock<IConfiguration>();
        _loggerMock = new Mock<ILogger<PaymentService>>();

        _configMock.Setup(c => c.GetSection("SecuritySettings:PaymentRateLimit").Value)
            .Returns("20");

        _paymentService = new PaymentService(
            _paymentRepositoryMock.Object,
            _invoiceRepositoryMock.Object,
            _cache,
            _configMock.Object,
            _loggerMock.Object
        );
    }

    [Fact]
    public async Task ProcessPaymentAsync_WithUnauthorizedClient_Returns403()
    {
        // Arrange
        var clientId = "unauthorized_client";
        _paymentRepositoryMock.Setup(r => r.GetClientAuthDetailsAsync(clientId))
            .ReturnsAsync((PaymentClient?)null); // No MANH set

        var request = new PaymentRequest
        {
            PartnerTransactionId = "TXN-001",
            CustomerCode = 100,
            TotalAmount = 100000,
            Invoices = new List<PaymentInvoiceItemDto> { new PaymentInvoiceItemDto { InvoiceCode = 987, AmountApplied = 100000 } }
        };

        // Act
        var (statusCode, response, systemError) = await _paymentService.ProcessPaymentAsync(clientId, request);

        // Assert
        Assert.Equal(StatusCodes.Status403Forbidden, statusCode);
        Assert.Null(response);
        Assert.Equal("not_authorized_for_payment", systemError);
    }

    [Fact]
    public async Task ProcessPaymentAsync_WithIdempotentReplay_ReturnsStoredResponse()
    {
        // Arrange
        var clientId = "demo_partner";
        _paymentRepositoryMock.Setup(r => r.GetClientAuthDetailsAsync(clientId))
            .ReturnsAsync(new PaymentClient { ClientId = clientId, Manh = "VCB", PayId = 13 });

        var request = new PaymentRequest
        {
            PartnerTransactionId = "TXN-001",
            CustomerCode = 100,
            TotalAmount = 100000,
            Invoices = new List<PaymentInvoiceItemDto> { new PaymentInvoiceItemDto { InvoiceCode = 987, AmountApplied = 100000 } }
        };

        var existingRecord = new PaymentTransactionRecord
        {
            Id = 1234,
            ClientId = clientId,
            PartnerTransactionId = "TXN-001",
            Status = "SUCCESS",
            ErrorCode = null,
            ErrorMessage = null,
            ProcessedAt = DateTime.UtcNow
        };

        _paymentRepositoryMock.Setup(r => r.InsertProcessingTransactionAsync(
                clientId, request.PartnerTransactionId, request.GatewayTransactionId, 
                request.CustomerCode.Value, request.TotalAmount.Value, It.IsAny<DateTime>(), 
                request.PaymentProvider, request.PaymentChannel))
            .ReturnsAsync((1234, "ALREADY_COMPLETED", existingRecord));

        // Act
        var (statusCode, response, systemError) = await _paymentService.ProcessPaymentAsync(clientId, request);

        // Assert
        Assert.Equal(StatusCodes.Status200OK, statusCode);
        Assert.NotNull(response);
        Assert.Equal("SUCCESS", response.Status);
        Assert.Equal("PMT-000001234", response.ReceiptCode);
        Assert.Null(systemError);
    }

    [Fact]
    public async Task ProcessPaymentAsync_WithIneligibleCustomer_ReturnsFailedStatus()
    {
        // Arrange
        var clientId = "demo_partner";
        _paymentRepositoryMock.Setup(r => r.GetClientAuthDetailsAsync(clientId))
            .ReturnsAsync(new PaymentClient { ClientId = clientId, Manh = "VCB", PayId = 13 });

        var request = new PaymentRequest
        {
            PartnerTransactionId = "TXN-002",
            CustomerCode = 101, // CUP status
            TotalAmount = 100000,
            Invoices = new List<PaymentInvoiceItemDto> { new PaymentInvoiceItemDto { InvoiceCode = 987, AmountApplied = 100000 } }
        };

        _paymentRepositoryMock.Setup(r => r.InsertProcessingTransactionAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), 
                It.IsAny<int>(), It.IsAny<long>(), It.IsAny<DateTime>(), 
                It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync((100, "NEW", null));

        _invoiceRepositoryMock.Setup(r => r.GetCustomerByIdAsync(101))
            .ReturnsAsync(new Customer { Idkh = 101, Ttsd = "CUP", GcdbKdpt = false });

        // Act
        var (statusCode, response, systemError) = await _paymentService.ProcessPaymentAsync(clientId, request);

        // Assert
        Assert.Equal(StatusCodes.Status200OK, statusCode);
        Assert.NotNull(response);
        Assert.Equal("FAILED", response.Status);
        Assert.Null(response.ReceiptCode);
        Assert.Equal("customer_not_eligible", response.ErrorCode);
    }

    [Fact]
    public async Task ProcessPaymentAsync_WithEmptyInvoices_ReturnsFailedStatus()
    {
        // Arrange
        var clientId = "demo_partner";
        _paymentRepositoryMock.Setup(r => r.GetClientAuthDetailsAsync(clientId))
            .ReturnsAsync(new PaymentClient { ClientId = clientId, Manh = "VCB", PayId = 13 });

        var request = new PaymentRequest
        {
            PartnerTransactionId = "TXN-003",
            CustomerCode = 100,
            TotalAmount = 100000,
            Invoices = new List<PaymentInvoiceItemDto>() // Empty
        };

        _paymentRepositoryMock.Setup(r => r.InsertProcessingTransactionAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), 
                It.IsAny<int>(), It.IsAny<long>(), It.IsAny<DateTime>(), 
                It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync((101, "NEW", null));

        // Act
        var (statusCode, response, systemError) = await _paymentService.ProcessPaymentAsync(clientId, request);

        // Assert
        Assert.Equal(StatusCodes.Status200OK, statusCode);
        Assert.NotNull(response);
        Assert.Equal("FAILED", response.Status);
        Assert.Equal("invalid_request", response.ErrorCode);
    }

    [Fact]
    public async Task ProcessPaymentAsync_WithDuplicateInvoiceInRequest_ReturnsFailedStatus()
    {
        // Arrange
        var clientId = "demo_partner";
        _paymentRepositoryMock.Setup(r => r.GetClientAuthDetailsAsync(clientId))
            .ReturnsAsync(new PaymentClient { ClientId = clientId, Manh = "VCB", PayId = 13 });

        var request = new PaymentRequest
        {
            PartnerTransactionId = "TXN-004",
            CustomerCode = 100,
            TotalAmount = 200000,
            Invoices = new List<PaymentInvoiceItemDto>
            {
                new PaymentInvoiceItemDto { InvoiceCode = 987, AmountApplied = 100000 },
                new PaymentInvoiceItemDto { InvoiceCode = 987, AmountApplied = 100000 } // Duplicate
            }
        };

        _paymentRepositoryMock.Setup(r => r.InsertProcessingTransactionAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), 
                It.IsAny<int>(), It.IsAny<long>(), It.IsAny<DateTime>(), 
                It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync((102, "NEW", null));

        // Act
        var (statusCode, response, systemError) = await _paymentService.ProcessPaymentAsync(clientId, request);

        // Assert
        Assert.Equal(StatusCodes.Status200OK, statusCode);
        Assert.NotNull(response);
        Assert.Equal("FAILED", response.Status);
        Assert.Equal("duplicate_invoice_in_request", response.ErrorCode);
    }

    [Fact]
    public async Task ProcessPaymentAsync_WithAmountMismatch_ReturnsFailedStatus()
    {
        // Arrange
        var clientId = "demo_partner";
        _paymentRepositoryMock.Setup(r => r.GetClientAuthDetailsAsync(clientId))
            .ReturnsAsync(new PaymentClient { ClientId = clientId, Manh = "VCB", PayId = 13 });

        var request = new PaymentRequest
        {
            PartnerTransactionId = "TXN-005",
            CustomerCode = 100,
            TotalAmount = 300000, // Total 300k
            Invoices = new List<PaymentInvoiceItemDto>
            {
                new PaymentInvoiceItemDto { InvoiceCode = 987, AmountApplied = 100000 } // Sum 100k
            }
        };

        _paymentRepositoryMock.Setup(r => r.InsertProcessingTransactionAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), 
                It.IsAny<int>(), It.IsAny<long>(), It.IsAny<DateTime>(), 
                It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync((103, "NEW", null));

        _invoiceRepositoryMock.Setup(r => r.GetCustomerByIdAsync(100))
            .ReturnsAsync(new Customer { Idkh = 100, Ttsd = "OK", GcdbKdpt = false });

        // Act
        var (statusCode, response, systemError) = await _paymentService.ProcessPaymentAsync(clientId, request);

        // Assert
        Assert.Equal(StatusCodes.Status200OK, statusCode);
        Assert.NotNull(response);
        Assert.Equal("FAILED", response.Status);
        Assert.Equal("amount_mismatch", response.ErrorCode);
    }

    [Fact]
    public async Task ProcessPaymentAsync_WithPhaseBAlreadyPaidInvoice_ReturnsFailedStatus()
    {
        // Arrange
        var clientId = "demo_partner";
        _paymentRepositoryMock.Setup(r => r.GetClientAuthDetailsAsync(clientId))
            .ReturnsAsync(new PaymentClient { ClientId = clientId, Manh = "VCB", PayId = 13 });

        var request = new PaymentRequest
        {
            PartnerTransactionId = "TXN-006",
            CustomerCode = 100,
            TotalAmount = 100000,
            Invoices = new List<PaymentInvoiceItemDto> { new PaymentInvoiceItemDto { InvoiceCode = 987, AmountApplied = 100000 } }
        };

        _paymentRepositoryMock.Setup(r => r.InsertProcessingTransactionAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), 
                It.IsAny<int>(), It.IsAny<long>(), It.IsAny<DateTime>(), 
                It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync((104, "NEW", null));

        _invoiceRepositoryMock.Setup(r => r.GetCustomerByIdAsync(100))
            .ReturnsAsync(new Customer { Idkh = 100, Ttsd = "OK", GcdbKdpt = false });

        _paymentRepositoryMock.Setup(r => r.ExecutePaymentPhaseBAsync(
                It.IsAny<long>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<List<PaymentInvoiceItemDto>>()))
            .ReturnsAsync((false, "already_paid", "Hóa đơn 987 đã được ghi nhận thanh toán trước đó."));

        // Act
        var (statusCode, response, systemError) = await _paymentService.ProcessPaymentAsync(clientId, request);

        // Assert
        Assert.Equal(StatusCodes.Status200OK, statusCode);
        Assert.NotNull(response);
        Assert.Equal("FAILED", response.Status);
        Assert.Null(response.ReceiptCode);
        Assert.Equal("already_paid", response.ErrorCode);
    }

    [Fact]
    public async Task ProcessPaymentAsync_WithValidTransaction_ReturnsSuccess()
    {
        // Arrange
        var clientId = "demo_partner";
        _paymentRepositoryMock.Setup(r => r.GetClientAuthDetailsAsync(clientId))
            .ReturnsAsync(new PaymentClient { ClientId = clientId, Manh = "VCB", PayId = 13 });

        var request = new PaymentRequest
        {
            PartnerTransactionId = "TXN-007",
            CustomerCode = 100,
            TotalAmount = 150000,
            Invoices = new List<PaymentInvoiceItemDto> { new PaymentInvoiceItemDto { InvoiceCode = 987, AmountApplied = 150000 } }
        };

        _paymentRepositoryMock.Setup(r => r.InsertProcessingTransactionAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), 
                It.IsAny<int>(), It.IsAny<long>(), It.IsAny<DateTime>(), 
                It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync((12345, "NEW", null));

        _invoiceRepositoryMock.Setup(r => r.GetCustomerByIdAsync(100))
            .ReturnsAsync(new Customer { Idkh = 100, Ttsd = "OK", GcdbKdpt = false });

        _paymentRepositoryMock.Setup(r => r.ExecutePaymentPhaseBAsync(
                It.IsAny<long>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<List<PaymentInvoiceItemDto>>()))
            .ReturnsAsync((true, null, null));

        // Act
        var (statusCode, response, systemError) = await _paymentService.ProcessPaymentAsync(clientId, request);

        // Assert
        Assert.Equal(StatusCodes.Status200OK, statusCode);
        Assert.NotNull(response);
        Assert.Equal("SUCCESS", response.Status);
        Assert.Equal("PMT-000012345", response.ReceiptCode);
        Assert.Null(response.ErrorCode);
    }

    [Fact]
    public void IsRateLimited_AfterTwentyRequestsInSameMinute_ReturnsTrue()
    {
        // Arrange
        var clientId = "demo_partner";

        // Act
        for (int i = 0; i < 20; i++)
        {
            Assert.False(_paymentService.IsRateLimited(clientId));
        }

        // Assert
        Assert.True(_paymentService.IsRateLimited(clientId));
    }

    [Fact]
    public async Task ProcessPaymentAsync_WithOutOfOrderInvoices_ReturnsPriorInvoicesUnpaid()
    {
        // Arrange
        var clientId = "demo_partner";
        _paymentRepositoryMock.Setup(r => r.GetClientAuthDetailsAsync(clientId))
            .ReturnsAsync(new PaymentClient { ClientId = clientId, Manh = "VCB", PayId = 13, IsActive = true });

        var request = new PaymentRequest
        {
            PartnerTransactionId = "TXN-OUT-OF-ORDER",
            CustomerCode = 100,
            TotalAmount = 200000,
            Invoices = new List<PaymentInvoiceItemDto> { new PaymentInvoiceItemDto { InvoiceCode = 200, AmountApplied = 200000 } }
        };

        _paymentRepositoryMock.Setup(r => r.InsertProcessingTransactionAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), 
                It.IsAny<int>(), It.IsAny<long>(), It.IsAny<DateTime>(), 
                It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync((999, "NEW", null));

        _invoiceRepositoryMock.Setup(r => r.GetCustomerByIdAsync(100))
            .ReturnsAsync(new Customer { Idkh = 100, Ttsd = "OK", GcdbKdpt = false });

        _invoiceRepositoryMock.Setup(r => r.GetUnpaidInvoicesByCustomerIdAsync(100))
            .ReturnsAsync(new List<InvoiceDto>
            {
                new InvoiceDto { InvoiceCode = 100, InvoiceYear = 2026, InvoiceMonth = 5, AmountDue = 100000 },
                new InvoiceDto { InvoiceCode = 200, InvoiceYear = 2026, InvoiceMonth = 6, AmountDue = 200000 }
            });

        // Act
        var (statusCode, response, systemError) = await _paymentService.ProcessPaymentAsync(clientId, request);

        // Assert
        Assert.Equal(StatusCodes.Status200OK, statusCode);
        Assert.NotNull(response);
        Assert.Equal("FAILED", response.Status);
        Assert.Equal("prior_invoices_unpaid", response.ErrorCode);
    }

    [Fact]
    public async Task ProcessPaymentAsync_WithReversedArrayOrderInvoices_ReturnsSuccess()
    {
        // Arrange
        var clientId = "demo_partner";
        _paymentRepositoryMock.Setup(r => r.GetClientAuthDetailsAsync(clientId))
            .ReturnsAsync(new PaymentClient { ClientId = clientId, Manh = "VCB", PayId = 13, IsActive = true });

        // Request contains both 2 oldest invoices (100 for May 2026, 200 for June 2026) in reversed array order [200, 100]
        var request = new PaymentRequest
        {
            PartnerTransactionId = "TXN-REVERSED-ARRAY",
            CustomerCode = 100,
            TotalAmount = 300000,
            Invoices = new List<PaymentInvoiceItemDto>
            {
                new PaymentInvoiceItemDto { InvoiceCode = 200, AmountApplied = 200000 },
                new PaymentInvoiceItemDto { InvoiceCode = 100, AmountApplied = 100000 }
            }
        };

        _paymentRepositoryMock.Setup(r => r.InsertProcessingTransactionAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), 
                It.IsAny<int>(), It.IsAny<long>(), It.IsAny<DateTime>(), 
                It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync((999, "NEW", null));

        _invoiceRepositoryMock.Setup(r => r.GetCustomerByIdAsync(100))
            .ReturnsAsync(new Customer { Idkh = 100, Ttsd = "OK", GcdbKdpt = false });

        _invoiceRepositoryMock.Setup(r => r.GetUnpaidInvoicesByCustomerIdAsync(100))
            .ReturnsAsync(new List<InvoiceDto>
            {
                new InvoiceDto { InvoiceCode = 100, InvoiceYear = 2026, InvoiceMonth = 5, AmountDue = 100000 },
                new InvoiceDto { InvoiceCode = 200, InvoiceYear = 2026, InvoiceMonth = 6, AmountDue = 200000 }
            });

        _paymentRepositoryMock.Setup(r => r.ExecutePaymentPhaseBAsync(
                It.IsAny<long>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<List<PaymentInvoiceItemDto>>()))
            .ReturnsAsync((true, null, null));

        // Act
        var (statusCode, response, systemError) = await _paymentService.ProcessPaymentAsync(clientId, request);

        // Assert
        Assert.Equal(StatusCodes.Status200OK, statusCode);
        Assert.NotNull(response);
        Assert.Equal("SUCCESS", response.Status);
        Assert.Equal("PMT-000000999", response.ReceiptCode);
        Assert.Null(response.ErrorCode);
    }

    [Fact]
    public async Task ProcessPaymentAsync_WithPayUserNotFound_ReturnsPayUserNotFound()
    {
        // Arrange
        var clientId = "demo_partner";
        _paymentRepositoryMock.Setup(r => r.GetClientAuthDetailsAsync(clientId))
            .ReturnsAsync(new PaymentClient { ClientId = clientId, Manh = "VCB", PayId = null, IsActive = true });

        var request = new PaymentRequest
        {
            PartnerTransactionId = "TXN-PAYID-MISSING",
            CustomerCode = 100,
            TotalAmount = 100000,
            Invoices = new List<PaymentInvoiceItemDto> { new PaymentInvoiceItemDto { InvoiceCode = 987, AmountApplied = 100000 } }
        };

        // Act
        var (statusCode, response, systemError) = await _paymentService.ProcessPaymentAsync(clientId, request);

        // Assert
        Assert.Equal(StatusCodes.Status403Forbidden, statusCode);
        Assert.Null(response);
        Assert.Equal("pay_user_not_found", systemError);
    }
}
