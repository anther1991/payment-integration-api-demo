using System.Text.Json.Serialization;

namespace API_Thanh_toan.Models;

public class PaymentResponse
{
    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("receiptCode")]
    public string? ReceiptCode { get; set; }

    [JsonPropertyName("processedAt")]
    public DateTime ProcessedAt { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("errorCode")]
    public string? ErrorCode { get; set; }

    [JsonPropertyName("errorMessage")]
    public string? ErrorMessage { get; set; }
}
