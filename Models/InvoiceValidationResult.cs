namespace API_Thanh_toan.Models;

public class InvoiceValidationResult
{
    public string? Ttsd { get; set; }
    public bool GcdbKdpt { get; set; }
    public bool Conno { get; set; }
    public string? CashManh { get; set; }
    public long Tongcong { get; set; }
    public string? EState { get; set; }
}
