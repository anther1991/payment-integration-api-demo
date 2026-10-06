using API_Thanh_toan.Models;

namespace API_Thanh_toan.Services;

public interface IInvoiceValidationService
{
    bool IsRateLimited(string clientId);
    Task<ValidateInvoiceResponse> ValidateInvoiceAsync(int customerCode, int invoiceCode);
}
