using API_Thanh_toan.Models;

namespace API_Thanh_toan.Services;

public interface IInvoiceLookupService
{
    bool IsRateLimited(string clientId);
    void DetectSequentialQueryPattern(string clientId, int customerCode, string sourceIp);
    Task<CustomerInvoicesResponse?> LookupInvoicesAsync(int customerCode);
}
