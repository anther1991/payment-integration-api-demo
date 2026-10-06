using API_Thanh_toan.Models;

namespace API_Thanh_toan.Services;

public interface IPaymentService
{
    bool IsRateLimited(string clientId);
    Task<(int StatusCode, PaymentResponse? Response, string? SystemError)> ProcessPaymentAsync(string clientId, PaymentRequest request);
}
