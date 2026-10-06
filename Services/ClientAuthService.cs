using System.Security.Cryptography;
using System.Text;
using API_Thanh_toan.Data;
using API_Thanh_toan.Models;
using Microsoft.Extensions.Caching.Memory;

namespace API_Thanh_toan.Services;

public class ClientAuthService : IClientAuthService
{
    private readonly IPaymentClientRepository _repository;
    private readonly IMemoryCache _cache;
    private readonly int _maxFailedAttempts;
    private static readonly TimeSpan BlockWindow = TimeSpan.FromMinutes(1);

    // Constant dummy values for timing attack mitigation
    private static readonly string DummySalt = "68c6e26927d6d7a46cbdf6bb5c66b1a37c95e1e5ef686a3479cb256b820a2835";
    private static readonly string DummyHash = "9d6b797cd2b2dcfdf45452f1efdfbc4410efec2efc45d34208a0ab4e8d249fef";

    public ClientAuthService(IPaymentClientRepository repository, IMemoryCache cache, Microsoft.Extensions.Configuration.IConfiguration configuration)
    {
        _repository = repository;
        _cache = cache;
        _maxFailedAttempts = int.TryParse(configuration["SecuritySettings:OAuthFailedRateLimit"], out var val) ? val : 5;
    }

    public (string? clientId, string? clientSecret) ParseBasicAuthHeader(string authorizationHeader)
    {
        if (string.IsNullOrWhiteSpace(authorizationHeader) || !authorizationHeader.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
        {
            return (null, null);
        }

        try
        {
            var parameter = authorizationHeader.Substring("Basic ".Length).Trim();
            var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(parameter));
            var parts = decoded.Split(':', 2);
            if (parts.Length == 2)
            {
                return (parts[0], parts[1]);
            }
        }
        catch
        {
            // Invalid base64 or format
        }

        return (null, null);
    }

    public async Task<bool> AuthenticateAsync(string clientId, string clientSecret)
    {
        // 1. Fetch client from DB
        var client = await _repository.GetByIdAsync(clientId);

        // 2. Perform authentication logic
        if (client != null && client.IsActive)
        {
            // Compute hash for received secret using client's salt from DB
            var computedHash = ComputeSha256Hash(clientSecret, client.ClientSecretSalt);

            // Compare computed hash with stored hash in constant time
            var isValid = ConstantTimeCompare(computedHash, client.ClientSecretHash);
            return isValid;
        }

        // 3. Timing attack mitigation: if client doesn't exist or is inactive,
        // we still hash the received secret with the dummy salt and compare with the dummy hash in constant time.
        var fallbackHash = ComputeSha256Hash(clientSecret, DummySalt);
        _ = ConstantTimeCompare(fallbackHash, DummyHash);

        return false;
    }

    public bool IsRateLimited(string clientId, string sourceIp)
    {
        if (string.IsNullOrWhiteSpace(clientId)) return false;

        var cacheKey = GetRateLimitCacheKey(clientId, sourceIp);
        if (_cache.TryGetValue<int>(cacheKey, out var failedCount))
        {
            return failedCount >= _maxFailedAttempts;
        }

        return false;
    }

    public void IncrementFailedAttempts(string clientId, string sourceIp)
    {
        if (string.IsNullOrWhiteSpace(clientId)) return;

        var cacheKey = GetRateLimitCacheKey(clientId, sourceIp);
        var failedCount = 0;

        if (_cache.TryGetValue<int>(cacheKey, out var currentCount))
        {
            failedCount = currentCount;
        }

        failedCount++;

        // Keep the existing sliding/absolute expiration window of 1 minute from the first failure
        _cache.Set(cacheKey, failedCount, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = BlockWindow
        });
    }

    private static string GetRateLimitCacheKey(string clientId, string sourceIp) 
        => $"rate-limit:failed:{(string.IsNullOrWhiteSpace(sourceIp) ? "unknown" : sourceIp.Trim())}:{clientId.ToLowerInvariant()}";

    private static string ComputeSha256Hash(string secret, string salt)
    {
        var inputBytes = Encoding.UTF8.GetBytes(secret + salt);
        var hashBytes = SHA256.HashData(inputBytes);
        return ConvertToHex(hashBytes);
    }

    private static string ConvertToHex(byte[] bytes)
    {
        var sb = new StringBuilder(bytes.Length * 2);
        foreach (var b in bytes)
        {
            sb.Append(b.ToString("x2"));
        }
        return sb.ToString();
    }

    private static bool ConstantTimeCompare(string a, string b)
    {
        var aBytes = Encoding.UTF8.GetBytes(a);
        var bBytes = Encoding.UTF8.GetBytes(b);
        return CryptographicOperations.FixedTimeEquals(aBytes, bBytes);
    }
}
