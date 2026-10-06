using API_Thanh_toan.Models;

namespace API_Thanh_toan.Data;

public interface IPaymentLinkTokenRepository
{
    Task<PaymentLinkToken?> GetTokenRecordAsync(string token);
}
