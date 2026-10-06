using API_Thanh_toan.Data;
using API_Thanh_toan.Models;
using Microsoft.Extensions.Caching.Memory;

namespace API_Thanh_toan.Services;

public class PaymentStatusService : IPaymentStatusService
{
    private readonly IPaymentStatusRepository _repository;
    private readonly IMemoryCache _cache;
    private readonly int _rateLimitThreshold;

    public PaymentStatusService(IPaymentStatusRepository repository, IMemoryCache cache, Microsoft.Extensions.Configuration.IConfiguration configuration)
    {
        _repository = repository;
        _cache = cache;
        _rateLimitThreshold = int.TryParse(configuration["SecuritySettings:PaymentStatusRateLimit"], out var val) ? val : 30;
    }

    public bool IsRateLimited(string clientId)
    {
        if (string.IsNullOrWhiteSpace(clientId)) return false;

        // Fixed-window rate limiting per minute
        var currentMinute = DateTime.UtcNow.ToString("yyyyMMddHHmm");
        var key = $"rate-limit:payment-status:{clientId.ToLowerInvariant()}:{currentMinute}";

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

    public async Task<(int StatusCode, PaymentResponse? Response, string? ErrorCode)> InquirePaymentStatusAsync(string clientId, string partnerTransactionId)
    {
        if (string.IsNullOrWhiteSpace(partnerTransactionId) || partnerTransactionId.Length > 100)
        {
            return (StatusCodes.Status400BadRequest, null, "invalid_request");
        }

        var record = await _repository.GetPaymentTransactionStatusAsync(clientId, partnerTransactionId);
        if (record == null)
        {
            return (StatusCodes.Status404NotFound, null, "transaction_not_found");
        }

        if (record.Status == "SUCCESS")
        {
            return (StatusCodes.Status200OK, new PaymentResponse
            {
                Status = "SUCCESS",
                ReceiptCode = $"PMT-{record.Id:D9}",
                ProcessedAt = record.ProcessedAt,
                ErrorCode = null,
                ErrorMessage = null
            }, null);
        }

        if (record.Status == "FAILED")
        {
            return (StatusCodes.Status200OK, new PaymentResponse
            {
                Status = "FAILED",
                ReceiptCode = null,
                ProcessedAt = record.ProcessedAt,
                ErrorCode = record.ErrorCode,
                ErrorMessage = record.ErrorMessage
            }, null);
        }

        // Status is PROCESSING
        return (StatusCodes.Status200OK, new PaymentResponse
        {
            Status = "PROCESSING",
            ReceiptCode = null,
            ProcessedAt = record.ProcessedAt,
            ErrorCode = null,
            ErrorMessage = null
        }, null);
    }
}
