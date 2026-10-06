using API_Thanh_toan.Data;
using API_Thanh_toan.Models;
using API_Thanh_toan.Services;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Moq;
using Xunit;

namespace API_Thanh_toan.Tests;

public class ClientAuthServiceTests
{
    private readonly Mock<IPaymentClientRepository> _repositoryMock;
    private readonly IMemoryCache _cache;
    private readonly Mock<IConfiguration> _configMock;
    private readonly ClientAuthService _authService;

    public ClientAuthServiceTests()
    {
        _repositoryMock = new Mock<IPaymentClientRepository>();
        _cache = new MemoryCache(new MemoryCacheOptions());
        _configMock = new Mock<IConfiguration>();

        _configMock.Setup(c => c.GetSection("SecuritySettings:OAuthFailedRateLimit").Value)
            .Returns("5");

        _authService = new ClientAuthService(_repositoryMock.Object, _cache, _configMock.Object);
    }

    [Fact]
    public async Task AuthenticateAsync_WithCorrectCredentials_ReturnsTrue()
    {
        // Arrange
        var clientId = "test-client";
        var clientSecret = "correct-secret";
        var salt = "test-salt";
        var hash = ComputeSha256("correct-secret", salt);

        _repositoryMock.Setup(r => r.GetByIdAsync(clientId))
            .ReturnsAsync(new PaymentClient
            {
                ClientId = clientId,
                ClientSecretSalt = salt,
                ClientSecretHash = hash,
                IsActive = true
            });

        // Act
        var result = await _authService.AuthenticateAsync(clientId, clientSecret);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task AuthenticateAsync_WithIncorrectSecret_ReturnsFalse()
    {
        // Arrange
        var clientId = "test-client";
        var clientSecret = "wrong-secret";
        var salt = "test-salt";
        var hash = ComputeSha256("correct-secret", salt);

        _repositoryMock.Setup(r => r.GetByIdAsync(clientId))
            .ReturnsAsync(new PaymentClient
            {
                ClientId = clientId,
                ClientSecretSalt = salt,
                ClientSecretHash = hash,
                IsActive = true
            });

        // Act
        var result = await _authService.AuthenticateAsync(clientId, clientSecret);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task AuthenticateAsync_WithClientNotFound_ReturnsFalse()
    {
        // Arrange
        var clientId = "non-existent-client";
        var clientSecret = "any-secret";

        _repositoryMock.Setup(r => r.GetByIdAsync(clientId))
            .ReturnsAsync((PaymentClient?)null);

        // Act
        var result = await _authService.AuthenticateAsync(clientId, clientSecret);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task AuthenticateAsync_WithInactiveClient_ReturnsFalse()
    {
        // Arrange
        var clientId = "inactive-client";
        var clientSecret = "correct-secret";
        var salt = "test-salt";
        var hash = ComputeSha256("correct-secret", salt);

        _repositoryMock.Setup(r => r.GetByIdAsync(clientId))
            .ReturnsAsync(new PaymentClient
            {
                ClientId = clientId,
                ClientSecretSalt = salt,
                ClientSecretHash = hash,
                IsActive = false
            });

        // Act
        var result = await _authService.AuthenticateAsync(clientId, clientSecret);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void IsRateLimited_AfterFiveFailedAttemptsPerIP_ReturnsTrueOnlyForThatIP()
    {
        // Arrange
        var clientId = "brute-force-client";
        var attackerIp = "192.168.1.100";
        var partnerIp = "10.0.0.1";

        // Act & Assert for Attacker IP
        for (int i = 0; i < 5; i++)
        {
            Assert.False(_authService.IsRateLimited(clientId, attackerIp));
            _authService.IncrementFailedAttempts(clientId, attackerIp);
        }

        // Attacker IP should be blocked
        Assert.True(_authService.IsRateLimited(clientId, attackerIp));

        // Partner IP with the same clientId should NOT be blocked
        Assert.False(_authService.IsRateLimited(clientId, partnerIp));
    }

    private static string ComputeSha256(string secret, string salt)
    {
        var inputBytes = System.Text.Encoding.UTF8.GetBytes(secret + salt);
        var hashBytes = System.Security.Cryptography.SHA256.HashData(inputBytes);
        var sb = new System.Text.StringBuilder(hashBytes.Length * 2);
        foreach (var b in hashBytes)
        {
            sb.Append(b.ToString("x2"));
        }
        return sb.ToString();
    }
}
