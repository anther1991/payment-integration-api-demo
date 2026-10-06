using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using API_Thanh_toan.Data;
using API_Thanh_toan.Infrastructure;
using API_Thanh_toan.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace API_Thanh_toan.Tests;

public class HmacSignatureFilterTests
{
    private readonly Mock<IPaymentClientRepository> _clientRepoMock;
    private readonly Mock<IDataProtectionHelper> _protectionHelperMock;
    private readonly Mock<ILogger<HmacSignatureFilter>> _loggerMock;
    private readonly HmacSignatureFilter _filter;

    public HmacSignatureFilterTests()
    {
        _clientRepoMock = new Mock<IPaymentClientRepository>();
        _protectionHelperMock = new Mock<IDataProtectionHelper>();
        _loggerMock = new Mock<ILogger<HmacSignatureFilter>>();

        _filter = new HmacSignatureFilter(
            _clientRepoMock.Object,
            _protectionHelperMock.Object,
            _loggerMock.Object
        );
    }

    [Fact]
    public async Task OnActionExecutionAsync_WithExpiredTimestamp_Returns401TimestampExpired()
    {
        // Arrange
        var httpContext = new DefaultHttpContext();
        var expiredTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 600; // 10 minutes ago
        httpContext.Request.Headers["X-Timestamp"] = expiredTimestamp.ToString();

        var actionContext = new ActionContext(httpContext, new RouteData(), new ActionDescriptor());
        var context = new ActionExecutingContext(actionContext, new List<IFilterMetadata>(), new Dictionary<string, object?>(), new object());
        var nextExecuted = false;

        // Act
        await _filter.OnActionExecutionAsync(context, () => { nextExecuted = true; return Task.FromResult<ActionExecutedContext>(null!); });

        // Assert
        Assert.False(nextExecuted);
        var result = Assert.IsType<UnauthorizedObjectResult>(context.Result);
        Assert.Equal(StatusCodes.Status401Unauthorized, result.StatusCode);
    }

    [Fact]
    public async Task OnActionExecutionAsync_WithInvalidSignature_Returns401InvalidSignature()
    {
        // Arrange
        var httpContext = new DefaultHttpContext();
        var nowTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        httpContext.Request.Headers["X-Timestamp"] = nowTimestamp.ToString();
        httpContext.Request.Headers["X-Signature"] = "invalid_signature_hex_1234567890abcdef1234567890abcdef1234567890abcdef";
        httpContext.Request.Method = "POST";
        httpContext.Request.Path = "/integration/v1/payments";

        var claims = new[] { new Claim("client_id", "demo_partner") };
        var identity = new ClaimsIdentity(claims, "TestAuth");
        httpContext.User = new ClaimsPrincipal(identity);

        var requestBody = "{\"customerCode\":100,\"totalAmount\":100000}";
        httpContext.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(requestBody));

        _clientRepoMock.Setup(r => r.GetByIdAsync("demo_partner"))
            .ReturnsAsync(new PaymentClient
            {
                ClientId = "demo_partner",
                IsActive = true,
                ClientSecretEncrypted = Encoding.UTF8.GetBytes("encrypted_secret")
            });

        _protectionHelperMock.Setup(p => p.DecryptSecret(It.IsAny<byte[]>()))
            .Returns("real_plain_secret_123");

        var actionContext = new ActionContext(httpContext, new RouteData(), new ActionDescriptor());
        var context = new ActionExecutingContext(actionContext, new List<IFilterMetadata>(), new Dictionary<string, object?>(), new object());
        var nextExecuted = false;

        // Act
        await _filter.OnActionExecutionAsync(context, () => { nextExecuted = true; return Task.FromResult<ActionExecutedContext>(null!); });

        // Assert
        Assert.False(nextExecuted);
        var result = Assert.IsType<UnauthorizedObjectResult>(context.Result);
        Assert.Equal(StatusCodes.Status401Unauthorized, result.StatusCode);
    }

    [Fact]
    public async Task OnActionExecutionAsync_WithValidSignature_CallsNextDelegate()
    {
        // Arrange
        var httpContext = new DefaultHttpContext();
        var nowTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var plainSecret = "real_plain_secret_123";
        var requestBody = "{\"customerCode\":100,\"totalAmount\":100000}";

        var stringToSign = $"POST\n/integration/v1/payments\n{nowTimestamp}\n{requestBody}";
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(plainSecret));
        var validSignature = Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(stringToSign))).ToLowerInvariant();

        httpContext.Request.Headers["X-Timestamp"] = nowTimestamp.ToString();
        httpContext.Request.Headers["X-Signature"] = validSignature;
        httpContext.Request.Method = "POST";
        httpContext.Request.Path = "/integration/v1/payments";

        var claims = new[] { new Claim("client_id", "demo_partner") };
        var identity = new ClaimsIdentity(claims, "TestAuth");
        httpContext.User = new ClaimsPrincipal(identity);

        httpContext.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(requestBody));

        _clientRepoMock.Setup(r => r.GetByIdAsync("demo_partner"))
            .ReturnsAsync(new PaymentClient
            {
                ClientId = "demo_partner",
                IsActive = true,
                ClientSecretEncrypted = Encoding.UTF8.GetBytes("encrypted_secret")
            });

        _protectionHelperMock.Setup(p => p.DecryptSecret(It.IsAny<byte[]>()))
            .Returns(plainSecret);

        var actionContext = new ActionContext(httpContext, new RouteData(), new ActionDescriptor());
        var context = new ActionExecutingContext(actionContext, new List<IFilterMetadata>(), new Dictionary<string, object?>(), new object());
        var nextExecuted = false;

        // Act
        await _filter.OnActionExecutionAsync(context, () => {
            nextExecuted = true;
            return Task.FromResult(new ActionExecutedContext(actionContext, new List<IFilterMetadata>(), new object()));
        });

        // Assert
        Assert.True(nextExecuted);
        Assert.Null(context.Result);
    }
}
