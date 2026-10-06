using API_Thanh_toan.Models;

namespace API_Thanh_toan.Data;

public class PaymentTransactionRecord
{
    public long Id { get; set; }
    public string ClientId { get; set; } = string.Empty;
    public string PartnerTransactionId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime ProcessedAt { get; set; }
}

public interface IPaymentRepository
{
    Task<PaymentClient?> GetClientAuthDetailsAsync(string clientId);
    Task<(long Id, string PhaseAResult, PaymentTransactionRecord? ExistingRecord)> InsertProcessingTransactionAsync(
        string clientId, 
        string partnerTxId, 
        string? gatewayTxId, 
        int customerCode, 
        long totalAmount, 
        DateTime paymentTime, 
        string? paymentProvider, 
        string? paymentChannel);

    Task UpdateTransactionStatusAsync(long transactionId, string status, string? errorCode, string? errorMessage);

    Task<(bool Success, string? ErrorCode, string? ErrorMessage)> ExecutePaymentPhaseBAsync(
        long transactionId, 
        int payId,
        string clientManh, 
        int customerCode, 
        string? payerAccountHolder, 
        string? payerAccountNumber, 
        List<PaymentInvoiceItemDto> invoices);
}
