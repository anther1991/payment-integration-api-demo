using System.Text.Json.Serialization;

namespace API_Thanh_toan.Models;

public class CustomerInvoicesResponse
{
    [JsonPropertyName("customerCode")]
    public int CustomerCode { get; set; }

    [JsonPropertyName("customerName")]
    public string CustomerName { get; set; } = string.Empty;

    [JsonPropertyName("phoneNumber")]
    public string PhoneNumber { get; set; } = string.Empty;

    [JsonPropertyName("eligibleForCollection")]
    public bool EligibleForCollection { get; set; }

    [JsonPropertyName("totalAmountDue")]
    public long TotalAmountDue { get; set; }

    [JsonPropertyName("invoices")]
    public List<InvoiceDto> Invoices { get; set; } = new();
}
