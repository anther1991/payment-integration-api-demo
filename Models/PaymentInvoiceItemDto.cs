using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace API_Thanh_toan.Models;

public class PaymentInvoiceItemDto
{
    [Required]
    [JsonPropertyName("invoiceCode")]
    public int? InvoiceCode { get; set; }

    [Required]
    [JsonPropertyName("amountApplied")]
    public long? AmountApplied { get; set; }
}
