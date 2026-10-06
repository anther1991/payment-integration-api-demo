namespace API_Thanh_toan.Models;

public class PaymentLinkToken
{
    public string Token { get; set; } = string.Empty;
    public int CustomerCode { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; }
}
