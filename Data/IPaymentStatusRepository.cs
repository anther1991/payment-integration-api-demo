using API_Thanh_toan.Data;

namespace API_Thanh_toan.Data;

public interface IPaymentStatusRepository
{
    Task<PaymentTransactionRecord?> GetPaymentTransactionStatusAsync(string clientId, string partnerTransactionId);
}
