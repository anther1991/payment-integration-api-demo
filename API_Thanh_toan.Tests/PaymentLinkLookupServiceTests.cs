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

public class PaymentLinkLookupServiceTests
{
    private readonly Mock<IPaymentLinkTokenRepository> _tokenRepoMock;
    private readonly Mock<IInvoiceLookupService> _invoiceLookupServiceMock;
    private readonly IMemoryCache _cache;
    private readonly Mock<IConfiguration> _configMock;
    private readonly Mock<ILogger<PaymentLinkLookupService>> _loggerMock;
    private readonly PaymentLinkLookupService _service;

    public PaymentLinkLookupServiceTests()
    {
        _tokenRepoMock = new Mock<IPaymentLinkTokenRepository>();
        _invoiceLookupServiceMock = new Mock<IInvoiceLookupService>();
        _cache = new MemoryCache(new MemoryCacheOptions());
        _configMock = new Mock<IConfiguration>();
        _loggerMock = new Mock<ILogger<PaymentLinkLookupService>>();

        _configMock.Setup(c => c.GetSection("SecuritySettings:PaymentLinkRateLimit").Value)
            .Returns("5000");

        _service = new PaymentLinkLookupService(
            _tokenRepoMock.Object,
            _invoiceLookupServiceMock.Object,
            _cache,
            _configMock.Object,
            _loggerMock.Object
        );
    }

    [Fact]
    public async Task GetInvoicesByTokenAsync_WithNonExistentToken_Returns404InvalidToken()
    {
        // Arrange
        var token = "non_existent_token_123";
        _tokenRepoMock.Setup(r => r.GetTokenRecordAsync(token))
            .ReturnsAsync((PaymentLinkToken?)null);

        // Act
        var (statusCode, response, errorCode) = await _service.GetInvoicesByTokenAsync("demo_partner", token);

        // Assert
        Assert.Equal(StatusCodes.Status404NotFound, statusCode);
        Assert.Null(response);
        Assert.Equal("invalid_token", errorCode);
    }

    [Fact]
    public async Task GetInvoicesByTokenAsync_WithExpiredToken_Returns400TokenExpired()
    {
        // Arrange
        var token = "expired_token_123";
        _tokenRepoMock.Setup(r => r.GetTokenRecordAsync(token))
            .ReturnsAsync(new PaymentLinkToken
            {
                Token = token,
                CustomerCode = 100002,
                ExpiresAt = DateTime.UtcNow.AddMinutes(-5),
                CreatedAt = DateTime.UtcNow.AddDays(-61)
            });

        // Act
        var (statusCode, response, errorCode) = await _service.GetInvoicesByTokenAsync("demo_partner", token);

        // Assert
        Assert.Equal(StatusCodes.Status400BadRequest, statusCode);
        Assert.Null(response);
        Assert.Equal("token_expired", errorCode);
    }

    [Fact]
    public async Task GetInvoicesByTokenAsync_WithValidToken_Returns200WithInvoices()
    {
        // Arrange
        var token = "valid_token_123";
        _tokenRepoMock.Setup(r => r.GetTokenRecordAsync(token))
            .ReturnsAsync(new PaymentLinkToken
            {
                Token = token,
                CustomerCode = 100002,
                ExpiresAt = DateTime.UtcNow.AddDays(30),
                CreatedAt = DateTime.UtcNow.AddDays(-1)
            });

        var expectedResponse = new CustomerInvoicesResponse
        {
            CustomerCode = 100002,
            CustomerName = "TEST CUSTOMER",
            EligibleForCollection = true,
            TotalAmountDue = 40000,
            Invoices = new List<InvoiceDto>
            {
                new InvoiceDto { InvoiceCode = 900004, AmountDue = 20000 },
                new InvoiceDto { InvoiceCode = 900005, AmountDue = 20000 }
            }
        };

        _invoiceLookupServiceMock.Setup(s => s.LookupInvoicesAsync(100002))
            .ReturnsAsync(expectedResponse);

        // Act
        var (statusCode, response, errorCode) = await _service.GetInvoicesByTokenAsync("demo_partner", token);

        // Assert
        Assert.Equal(StatusCodes.Status200OK, statusCode);
        Assert.NotNull(response);
        Assert.Equal(100002, response!.CustomerCode);
        Assert.Equal(40000, response.TotalAmountDue);
        Assert.Null(errorCode);
    }

    [Fact]
    public void IsRateLimited_After5000RequestsInSameMinute_ReturnsTrue()
    {
        // Arrange
        var clientId = "demo_partner";

        // Act
        for (int i = 0; i < 5000; i++)
        {
            Assert.False(_service.IsRateLimited(clientId));
        }

        // Assert
        Assert.True(_service.IsRateLimited(clientId));
    }
}
