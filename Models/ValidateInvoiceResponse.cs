using System.Text.Json.Serialization;

namespace API_Thanh_toan.Models;

public class ValidateInvoiceResponse
{
    [JsonPropertyName("isValid")]
    public bool IsValid { get; set; }

    [JsonPropertyName("invoiceStatus")]
    public string? InvoiceStatus { get; set; }

    [JsonPropertyName("amountDue")]
    public long? AmountDue { get; set; }

    [JsonPropertyName("reason")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Reason { get; set; }
}
