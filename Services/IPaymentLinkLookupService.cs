using API_Thanh_toan.Models;

namespace API_Thanh_toan.Services;

public interface IPaymentLinkLookupService
{
    bool IsRateLimited(string clientId);
    Task<(int StatusCode, CustomerInvoicesResponse? Response, string? ErrorCode)> GetInvoicesByTokenAsync(string clientId, string token);
}
