using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace API_Thanh_toan.Models;

public class ValidateInvoiceRequest
{
    [Required]
    [JsonPropertyName("customerCode")]
    public int? CustomerCode { get; set; }

    [Required]
    [JsonPropertyName("invoiceCode")]
    public int? InvoiceCode { get; set; }
}
