using API_Thanh_toan.Data;
using API_Thanh_toan.Models;
using API_Thanh_toan.Services;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace API_Thanh_toan.Tests;

public class InvoiceLookupServiceTests
{
    private readonly Mock<IInvoiceRepository> _repositoryMock;
    private readonly IMemoryCache _cache;
    private readonly Mock<ILogger<InvoiceLookupService>> _loggerMock;
    private readonly Mock<IConfiguration> _configMock;
    private readonly InvoiceLookupService _lookupService;

    public InvoiceLookupServiceTests()
    {
        _repositoryMock = new Mock<IInvoiceRepository>();
        _cache = new MemoryCache(new MemoryCacheOptions());
        _loggerMock = new Mock<ILogger<InvoiceLookupService>>();
        
        // Mock configuration
        _configMock = new Mock<IConfiguration>();
        var configSectionMock = new Mock<IConfigurationSection>();
        configSectionMock.Setup(s => s.Value).Returns("5"); // SequentialThreshold = 5
        _configMock.Setup(c => c.GetSection("SecuritySettings:SequentialThreshold")).Returns(configSectionMock.Object);

        _lookupService = new InvoiceLookupService(
            _repositoryMock.Object, 
            _cache, 
            _configMock.Object, 
            _loggerMock.Object
        );
    }

    [Fact]
    public async Task LookupInvoicesAsync_WithNonExistentCustomer_ReturnsNull()
    {
        // Arrange
        var customerCode = 999;
        _repositoryMock.Setup(r => r.GetCustomerByIdAsync(customerCode))
            .ReturnsAsync((Customer?)null);

        // Act
        var result = await _lookupService.LookupInvoicesAsync(customerCode);

        // Assert
        Assert.Null(result);
        _repositoryMock.Verify(r => r.GetUnpaidInvoicesByCustomerIdAsync(It.IsAny<int>()), Times.Never);
    }

    [Theory]
    [InlineData("CUP", false)]
    [InlineData("OK", true)] // GCDB_KDPT = true
    public async Task LookupInvoicesAsync_WithIneligibleCustomer_ReturnsEligibleFalseAndNoInvoices(string ttsd, bool gcdbKdpt)
    {
        // Arrange
        var customerCode = 123;
        _repositoryMock.Setup(r => r.GetCustomerByIdAsync(customerCode))
            .ReturnsAsync(new Customer
            {
                Idkh = customerCode,
                Tenkh = "Nguyen Van A",
                Sodt = "0900000000",
                Ttsd = ttsd,
                GcdbKdpt = gcdbKdpt
            });

        // Act
        var result = await _lookupService.LookupInvoicesAsync(customerCode);

        // Assert
        Assert.NotNull(result);
        Assert.False(result.EligibleForCollection);
        Assert.Equal(0, result.TotalAmountDue);
        Assert.Empty(result.Invoices);
        _repositoryMock.Verify(r => r.GetUnpaidInvoicesByCustomerIdAsync(It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task LookupInvoicesAsync_WithEligibleAndNoInvoices_ReturnsEligibleTrueAndEmptyInvoices()
    {
        // Arrange
        var customerCode = 123;
        _repositoryMock.Setup(r => r.GetCustomerByIdAsync(customerCode))
            .ReturnsAsync(new Customer
            {
                Idkh = customerCode,
                Tenkh = "Nguyen Van A",
                Sodt = "0900000000",
                Ttsd = "OK",
                GcdbKdpt = false
            });

        _repositoryMock.Setup(r => r.GetUnpaidInvoicesByCustomerIdAsync(customerCode))
            .ReturnsAsync(new List<InvoiceDto>());

        // Act
        var result = await _lookupService.LookupInvoicesAsync(customerCode);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.EligibleForCollection);
        Assert.Equal(0, result.TotalAmountDue);
        Assert.Empty(result.Invoices);
        _repositoryMock.Verify(r => r.GetUnpaidInvoicesByCustomerIdAsync(customerCode), Times.Once);
    }

    [Fact]
    public async Task LookupInvoicesAsync_WithEligibleAndUnpaidInvoices_ReturnsCorrectDetailsAndFormatsPeriod()
    {
        // Arrange
        var customerCode = 123;
        _repositoryMock.Setup(r => r.GetCustomerByIdAsync(customerCode))
            .ReturnsAsync(new Customer
            {
                Idkh = customerCode,
                Tenkh = "Nguyen Van A",
                Sodt = "0900000000",
                Ttsd = "OK",
                GcdbKdpt = false
            });

        var dbInvoices = new List<InvoiceDto>
        {
            new InvoiceDto
            {
                InvoiceCode = 987,
                InvoiceForm = "1",
                InvoiceSymbol = "C25TAB",
                InvoiceSerial = "000123",
                InvoiceYear = 2026,
                InvoiceMonth = 7,
                Address = "123 Street",
                AmountDue = 150000,
                IssuedDate = DateTime.Parse("2026-07-05")
            },
            new InvoiceDto
            {
                InvoiceCode = 988,
                InvoiceForm = "1",
                InvoiceSymbol = "C25TAB",
                InvoiceSerial = "000124",
                InvoiceYear = 2026,
                InvoiceMonth = 8,
                Address = "123 Street",
                AmountDue = 200000,
                IssuedDate = DateTime.Parse("2026-08-05")
            }
        };

        _repositoryMock.Setup(r => r.GetUnpaidInvoicesByCustomerIdAsync(customerCode))
            .ReturnsAsync(dbInvoices);

        // Act
        var result = await _lookupService.LookupInvoicesAsync(customerCode);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.EligibleForCollection);
        Assert.Equal(350000, result.TotalAmountDue);
        Assert.Equal(2, result.Invoices.Count);
        
        Assert.Equal("07/2026", result.Invoices[0].InvoicePeriod);
        Assert.Equal("08/2026", result.Invoices[1].InvoicePeriod);
    }

    [Fact]
    public void IsRateLimited_AfterThirtyRequestsInSameMinute_ReturnsTrue()
    {
        // Arrange
        var clientId = "demo-partner";

        // Act
        for (int i = 0; i < 30; i++)
        {
            Assert.False(_lookupService.IsRateLimited(clientId));
        }

        // Assert
        Assert.True(_lookupService.IsRateLimited(clientId));
    }

    [Fact]
    public void DetectSequentialQueryPattern_WithFiveConsecutiveIds_LogsWarning()
    {
        // Arrange
        var clientId = "crawler-client";
        var ip = "127.0.0.1";

        // Act
        _lookupService.DetectSequentialQueryPattern(clientId, 100, ip);
        _lookupService.DetectSequentialQueryPattern(clientId, 101, ip);
        _lookupService.DetectSequentialQueryPattern(clientId, 102, ip);
        _lookupService.DetectSequentialQueryPattern(clientId, 103, ip);

        // Verifying it has NOT logged a warning yet
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Sequential lookup pattern detected")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Never);

        // Triggering the 5th consecutive request
        _lookupService.DetectSequentialQueryPattern(clientId, 104, ip);

        // Assert - Warning should be logged once
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Sequential lookup pattern detected")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }
}
