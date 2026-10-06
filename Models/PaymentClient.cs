namespace API_Thanh_toan.Models;

public class PaymentClient
{
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecretSalt { get; set; } = string.Empty;
    public string ClientSecretHash { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public string? Manh { get; set; }
    public int? PayId { get; set; }
    public byte[]? ClientSecretEncrypted { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
}
