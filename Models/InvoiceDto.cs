using System.Text.Json.Serialization;

namespace API_Thanh_toan.Models;

public class InvoiceDto
{
    [JsonPropertyName("invoiceCode")]
    public int InvoiceCode { get; set; }

    [JsonPropertyName("invoiceForm")]
    public string InvoiceForm { get; set; } = string.Empty;

    [JsonPropertyName("invoiceSymbol")]
    public string InvoiceSymbol { get; set; } = string.Empty;

    [JsonPropertyName("invoiceSerial")]
    public string InvoiceSerial { get; set; } = string.Empty;

    [JsonPropertyName("invoicePeriod")]
    public string InvoicePeriod { get; set; } = string.Empty;

    [JsonPropertyName("address")]
    public string Address { get; set; } = string.Empty;

    [JsonPropertyName("amountDue")]
    public long AmountDue { get; set; }

    [JsonPropertyName("issuedDate")]
    public DateTimeOffset? IssuedDate { get; set; }

    [JsonIgnore]
    public int InvoiceYear { get; set; }

    [JsonIgnore]
    public int InvoiceMonth { get; set; }
}
