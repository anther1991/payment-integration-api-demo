using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace API_Thanh_toan.Models;

public class PaymentRequest
{
    [Required]
    [JsonPropertyName("partnerTransactionId")]
    public string PartnerTransactionId { get; set; } = string.Empty;

    [JsonPropertyName("gatewayTransactionId")]
    public string? GatewayTransactionId { get; set; }

    [Required]
    [JsonPropertyName("customerCode")]
    public int? CustomerCode { get; set; }

    [Required]
    [JsonPropertyName("totalAmount")]
    public long? TotalAmount { get; set; }

    [JsonPropertyName("paymentTime")]
    public DateTime? PaymentTime { get; set; }

    [JsonPropertyName("paymentProvider")]
    public string? PaymentProvider { get; set; }

    [JsonPropertyName("paymentChannel")]
    public string? PaymentChannel { get; set; }

    [JsonPropertyName("payerAccountHolder")]
    public string? PayerAccountHolder { get; set; }

    [JsonPropertyName("payerAccountNumber")]
    public string? PayerAccountNumber { get; set; }

    [JsonPropertyName("invoices")]
    public List<PaymentInvoiceItemDto> Invoices { get; set; } = new();
}
