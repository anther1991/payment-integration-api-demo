using API_Thanh_toan.Data;
using API_Thanh_toan.Models;
using Microsoft.Extensions.Caching.Memory;

namespace API_Thanh_toan.Services;

public class PaymentLinkLookupService : IPaymentLinkLookupService
{
    private readonly IPaymentLinkTokenRepository _tokenRepository;
    private readonly IInvoiceLookupService _invoiceLookupService;
    private readonly IMemoryCache _cache;
    private readonly ILogger<PaymentLinkLookupService> _logger;
    private readonly int _rateLimitThreshold;

    public PaymentLinkLookupService(
        IPaymentLinkTokenRepository tokenRepository,
        IInvoiceLookupService invoiceLookupService,
        IMemoryCache cache,
        IConfiguration configuration,
        ILogger<PaymentLinkLookupService> logger)
    {
        _tokenRepository = tokenRepository;
        _invoiceLookupService = invoiceLookupService;
        _cache = cache;
        _logger = logger;
        _rateLimitThreshold = int.TryParse(configuration["SecuritySettings:PaymentLinkRateLimit"], out var val) ? val : 5000;
    }

    public bool IsRateLimited(string clientId)
    {
        if (string.IsNullOrWhiteSpace(clientId)) return false;

        var currentMinute = DateTime.UtcNow.ToString("yyyyMMddHHmm");
        var key = $"rate-limit:payment-link-lookup:{clientId.ToLowerInvariant()}:{currentMinute}";

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

    public async Task<(int StatusCode, CustomerInvoicesResponse? Response, string? ErrorCode)> GetInvoicesByTokenAsync(string clientId, string token)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 64)
        {
            return (StatusCodes.Status404NotFound, null, "invalid_token");
        }

        var tokenRecord = await _tokenRepository.GetTokenRecordAsync(token);
        if (tokenRecord == null)
        {
            _logger.LogWarning("Payment link token lookup failed: Token '{Token}' not found. Request by '{ClientId}'.", token, clientId);
            return (StatusCodes.Status404NotFound, null, "invalid_token");
        }

        if (tokenRecord.ExpiresAt < DateTime.UtcNow)
        {
            _logger.LogWarning("Payment link token lookup failed: Token '{Token}' for Customer '{CustomerCode}' expired at '{ExpiresAt}'. Request by '{ClientId}'.",
                token, tokenRecord.CustomerCode, tokenRecord.ExpiresAt, clientId);
            return (StatusCodes.Status400BadRequest, null, "token_expired");
        }

        var result = await _invoiceLookupService.LookupInvoicesAsync(tokenRecord.CustomerCode);
        if (result == null)
        {
            _logger.LogWarning("Payment link token lookup failed: Customer '{CustomerCode}' not found. Request by '{ClientId}'.",
                tokenRecord.CustomerCode, clientId);
            return (StatusCodes.Status404NotFound, null, "customer_not_found");
        }

        _logger.LogInformation("Payment link token lookup successful for Customer '{CustomerCode}' via Token by '{ClientId}'.",
            tokenRecord.CustomerCode, clientId);

        return (StatusCodes.Status200OK, result, null);
    }
}
