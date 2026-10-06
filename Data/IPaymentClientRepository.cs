using API_Thanh_toan.Models;

namespace API_Thanh_toan.Data;

public interface IPaymentClientRepository
{
    Task<PaymentClient?> GetByIdAsync(string clientId);
}
