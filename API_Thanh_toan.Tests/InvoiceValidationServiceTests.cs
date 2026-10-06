using API_Thanh_toan.Data;
using API_Thanh_toan.Models;
using API_Thanh_toan.Services;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Moq;
using Xunit;

namespace API_Thanh_toan.Tests;

public class InvoiceValidationServiceTests
{
    private readonly Mock<IInvoiceValidationRepository> _repositoryMock;
    private readonly IMemoryCache _cache;
    private readonly Mock<IConfiguration> _configMock;
    private readonly InvoiceValidationService _validationService;

    public InvoiceValidationServiceTests()
    {
        _repositoryMock = new Mock<IInvoiceValidationRepository>();
        _cache = new MemoryCache(new MemoryCacheOptions());
        _configMock = new Mock<IConfiguration>();

        _configMock.Setup(c => c.GetSection("SecuritySettings:InvoiceValidationRateLimit").Value)
            .Returns("30");

        _validationService = new InvoiceValidationService(_repositoryMock.Object, _cache, _configMock.Object);
    }

    [Fact]
    public async Task ValidateInvoiceAsync_WithNonExistentInvoice_ReturnsInvoiceNotFound()
    {
        // Arrange
        var customerCode = 123;
        var invoiceCode = 999;
        _repositoryMock.Setup(r => r.ValidateInvoiceCrossCheckAsync(customerCode, invoiceCode))
            .ReturnsAsync((InvoiceValidationResult?)null);

        // Act
        var response = await _validationService.ValidateInvoiceAsync(customerCode, invoiceCode);

        // Assert
        Assert.NotNull(response);
        Assert.False(response.IsValid);
        Assert.Null(response.InvoiceStatus);
        Assert.Null(response.AmountDue);
        Assert.Equal("invoice_not_found", response.Reason);
    }

    [Theory]
    [InlineData("CUP", false)]
    [InlineData("OK", true)]
    public async Task ValidateInvoiceAsync_WithIneligibleCustomer_ReturnsCustomerNotEligible(string ttsd, bool gcdbKdpt)
    {
        // Arrange
        var customerCode = 123;
        var invoiceCode = 987;
        _repositoryMock.Setup(r => r.ValidateInvoiceCrossCheckAsync(customerCode, invoiceCode))
            .ReturnsAsync(new InvoiceValidationResult
            {
                Ttsd = ttsd,
                GcdbKdpt = gcdbKdpt,
                Conno = true,
                CashManh = null,
                Tongcong = 150000,
                EState = "DONE"
            });

        // Act
        var response = await _validationService.ValidateInvoiceAsync(customerCode, invoiceCode);

        // Assert
        Assert.NotNull(response);
        Assert.False(response.IsValid);
        Assert.Null(response.InvoiceStatus);
        Assert.Null(response.AmountDue);
        Assert.Equal("customer_not_eligible", response.Reason);
    }

    [Theory]
    [InlineData(false, null)]      // CONNO = false (paid)
    [InlineData(true, "VNPAY123")] // cashMANH is not null (pending reconciliation)
    public async Task ValidateInvoiceAsync_WithAlreadyPaidInvoice_ReturnsAlreadyPaid(bool conno, string? cashManh)
    {
        // Arrange
        var customerCode = 123;
        var invoiceCode = 987;
        _repositoryMock.Setup(r => r.ValidateInvoiceCrossCheckAsync(customerCode, invoiceCode))
            .ReturnsAsync(new InvoiceValidationResult
            {
                Ttsd = "OK",
                GcdbKdpt = false,
                Conno = conno,
                CashManh = cashManh,
                Tongcong = 150000,
                EState = "DONE"
            });

        // Act
        var response = await _validationService.ValidateInvoiceAsync(customerCode, invoiceCode);

        // Assert
        Assert.NotNull(response);
        Assert.False(response.IsValid);
        Assert.Null(response.InvoiceStatus);
        Assert.Null(response.AmountDue);
        Assert.Equal("already_paid", response.Reason);
    }

    [Fact]
    public async Task ValidateInvoiceAsync_WithUnissuedInvoice_ReturnsInvoiceNotIssued()
    {
        // Arrange
        var customerCode = 123;
        var invoiceCode = 987;
        _repositoryMock.Setup(r => r.ValidateInvoiceCrossCheckAsync(customerCode, invoiceCode))
            .ReturnsAsync(new InvoiceValidationResult
            {
                Ttsd = "OK",
                GcdbKdpt = false,
                Conno = true,
                CashManh = null,
                Tongcong = 150000,
                EState = "NEW" // Not in DONE, CASTED
            });

        // Act
        var response = await _validationService.ValidateInvoiceAsync(customerCode, invoiceCode);

        // Assert
        Assert.NotNull(response);
        Assert.False(response.IsValid);
        Assert.Null(response.InvoiceStatus);
        Assert.Null(response.AmountDue);
        Assert.Equal("invoice_not_issued", response.Reason);
    }

    [Theory]
    [InlineData("DONE")]
    [InlineData("CASTED")]
    public async Task ValidateInvoiceAsync_WithValidUnpaidInvoice_ReturnsValidInvoice(string eState)
    {
        // Arrange
        var customerCode = 123;
        var invoiceCode = 987;
        _repositoryMock.Setup(r => r.ValidateInvoiceCrossCheckAsync(customerCode, invoiceCode))
            .ReturnsAsync(new InvoiceValidationResult
            {
                Ttsd = "OK",
                GcdbKdpt = false,
                Conno = true,
                CashManh = null,
                Tongcong = 250000,
                EState = eState
            });

        // Act
        var response = await _validationService.ValidateInvoiceAsync(customerCode, invoiceCode);

        // Assert
        Assert.NotNull(response);
        Assert.True(response.IsValid);
        Assert.Equal("UNPAID", response.InvoiceStatus);
        Assert.Equal(250000, response.AmountDue);
        Assert.Null(response.Reason);
    }

    [Fact]
    public void IsRateLimited_AfterThirtyRequestsInSameMinute_ReturnsTrue()
    {
        // Arrange
        var clientId = "demo-partner";

        // Act
        for (int i = 0; i < 30; i++)
        {
            Assert.False(_validationService.IsRateLimited(clientId));
        }

        // Assert
        Assert.True(_validationService.IsRateLimited(clientId));
    }
}
