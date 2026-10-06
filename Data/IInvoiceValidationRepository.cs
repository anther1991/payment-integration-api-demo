using API_Thanh_toan.Models;

namespace API_Thanh_toan.Data;

public interface IInvoiceValidationRepository
{
    Task<InvoiceValidationResult?> ValidateInvoiceCrossCheckAsync(int customerCode, int invoiceCode);
}
