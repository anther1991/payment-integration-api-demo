using API_Thanh_toan.Models;

namespace API_Thanh_toan.Services;

public interface IPaymentStatusService
{
    bool IsRateLimited(string clientId);
    Task<(int StatusCode, PaymentResponse? Response, string? ErrorCode)> InquirePaymentStatusAsync(string clientId, string partnerTransactionId);
}
