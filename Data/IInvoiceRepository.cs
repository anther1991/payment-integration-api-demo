using API_Thanh_toan.Models;

namespace API_Thanh_toan.Data;

public interface IInvoiceRepository
{
    Task<Customer?> GetCustomerByIdAsync(int customerCode);
    Task<IEnumerable<InvoiceDto>> GetUnpaidInvoicesByCustomerIdAsync(int customerCode);
}
