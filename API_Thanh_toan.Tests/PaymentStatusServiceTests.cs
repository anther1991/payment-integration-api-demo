using API_Thanh_toan.Data;
using API_Thanh_toan.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Moq;
using Xunit;

namespace API_Thanh_toan.Tests;

public class PaymentStatusServiceTests
{
    private readonly Mock<IPaymentStatusRepository> _repositoryMock;
    private readonly IMemoryCache _cache;
    private readonly Mock<IConfiguration> _configMock;
    private readonly PaymentStatusService _statusService;

    public PaymentStatusServiceTests()
    {
        _repositoryMock = new Mock<IPaymentStatusRepository>();
        _cache = new MemoryCache(new MemoryCacheOptions());
        _configMock = new Mock<IConfiguration>();

        _configMock.Setup(c => c.GetSection("SecuritySettings:PaymentStatusRateLimit").Value)
            .Returns("30");

        _statusService = new PaymentStatusService(_repositoryMock.Object, _cache, _configMock.Object);
    }

    [Fact]
    public async Task InquirePaymentStatusAsync_WithSuccessfulTransaction_ReturnsReceiptCode()
    {
        // Arrange
        var clientId = "demo_partner";
        var partnerTxId = "TXN-100";
        var record = new PaymentTransactionRecord
        {
            Id = 1234,
            ClientId = clientId,
            PartnerTransactionId = partnerTxId,
            Status = "SUCCESS",
            ErrorCode = null,
            ErrorMessage = null,
            ProcessedAt = DateTime.UtcNow
        };

        _repositoryMock.Setup(r => r.GetPaymentTransactionStatusAsync(clientId, partnerTxId))
            .ReturnsAsync(record);

        // Act
        var (statusCode, response, errorCode) = await _statusService.InquirePaymentStatusAsync(clientId, partnerTxId);

        // Assert
        Assert.Equal(StatusCodes.Status200OK, statusCode);
        Assert.NotNull(response);
        Assert.Equal("SUCCESS", response.Status);
        Assert.Equal("PMT-000001234", response.ReceiptCode);
        Assert.Null(errorCode);
    }

    [Fact]
    public async Task InquirePaymentStatusAsync_WithFailedTransaction_ReturnsErrorDetailsAndNullReceipt()
    {
        // Arrange
        var clientId = "demo_partner";
        var partnerTxId = "TXN-101";
        var record = new PaymentTransactionRecord
        {
            Id = 1235,
            ClientId = clientId,
            PartnerTransactionId = partnerTxId,
            Status = "FAILED",
            ErrorCode = "already_paid",
            ErrorMessage = "Hóa đơn đã được ghi nhận thanh toán trước đó.",
            ProcessedAt = DateTime.UtcNow
        };

        _repositoryMock.Setup(r => r.GetPaymentTransactionStatusAsync(clientId, partnerTxId))
            .ReturnsAsync(record);

        // Act
        var (statusCode, response, errorCode) = await _statusService.InquirePaymentStatusAsync(clientId, partnerTxId);

        // Assert
        Assert.Equal(StatusCodes.Status200OK, statusCode);
        Assert.NotNull(response);
        Assert.Equal("FAILED", response.Status);
        Assert.Null(response.ReceiptCode);
        Assert.Equal("already_paid", response.ErrorCode);
        Assert.Equal("Hóa đơn đã được ghi nhận thanh toán trước đó.", response.ErrorMessage);
    }

    [Fact]
    public async Task InquirePaymentStatusAsync_WithProcessingTransaction_ReturnsProcessingStatus()
    {
        // Arrange
        var clientId = "demo_partner";
        var partnerTxId = "TXN-102";
        var record = new PaymentTransactionRecord
        {
            Id = 1236,
            ClientId = clientId,
            PartnerTransactionId = partnerTxId,
            Status = "PROCESSING",
            ErrorCode = null,
            ErrorMessage = null,
            ProcessedAt = DateTime.UtcNow
        };

        _repositoryMock.Setup(r => r.GetPaymentTransactionStatusAsync(clientId, partnerTxId))
            .ReturnsAsync(record);

        // Act
        var (statusCode, response, errorCode) = await _statusService.InquirePaymentStatusAsync(clientId, partnerTxId);

        // Assert
        Assert.Equal(StatusCodes.Status200OK, statusCode);
        Assert.NotNull(response);
        Assert.Equal("PROCESSING", response.Status);
        Assert.Null(response.ReceiptCode);
    }

    [Fact]
    public async Task InquirePaymentStatusAsync_WithNonExistentTransaction_Returns404NotFound()
    {
        // Arrange
        var clientId = "demo_partner";
        var partnerTxId = "NON_EXISTENT_TXN";

        _repositoryMock.Setup(r => r.GetPaymentTransactionStatusAsync(clientId, partnerTxId))
            .ReturnsAsync((PaymentTransactionRecord?)null);

        // Act
        var (statusCode, response, errorCode) = await _statusService.InquirePaymentStatusAsync(clientId, partnerTxId);

        // Assert
        Assert.Equal(StatusCodes.Status404NotFound, statusCode);
        Assert.Null(response);
        Assert.Equal("transaction_not_found", errorCode);
    }

    [Fact]
    public async Task InquirePaymentStatusAsync_CrossClientAccess_Returns404NotFound()
    {
        // Arrange
        var requestingClientId = "momo_payment";
        var targetTxId = "TXN-BELONGS-TO-PARTNER";

        // Repository filters by WHERE ClientId = @clientId AND PartnerTransactionId = @partnerTxId
        // So for momo_payment, query returns null!
        _repositoryMock.Setup(r => r.GetPaymentTransactionStatusAsync(requestingClientId, targetTxId))
            .ReturnsAsync((PaymentTransactionRecord?)null);

        // Act
        var (statusCode, response, errorCode) = await _statusService.InquirePaymentStatusAsync(requestingClientId, targetTxId);

        // Assert
        Assert.Equal(StatusCodes.Status404NotFound, statusCode);
        Assert.Null(response);
        Assert.Equal("transaction_not_found", errorCode);
        _repositoryMock.Verify(r => r.GetPaymentTransactionStatusAsync("momo_payment", targetTxId), Times.Once);
    }

    [Fact]
    public void IsRateLimited_AfterThirtyRequestsInSameMinute_ReturnsTrue()
    {
        // Arrange
        var clientId = "demo_partner";

        // Act
        for (int i = 0; i < 30; i++)
        {
            Assert.False(_statusService.IsRateLimited(clientId));
        }

        // Assert
        Assert.True(_statusService.IsRateLimited(clientId));
    }
}
