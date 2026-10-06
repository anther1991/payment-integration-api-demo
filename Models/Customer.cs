namespace API_Thanh_toan.Models;

public class Customer
{
    public int Idkh { get; set; }
    public string Tenkh { get; set; } = string.Empty;
    public string Sodt { get; set; } = string.Empty;
    public string Ttsd { get; set; } = string.Empty;
    public bool GcdbKdpt { get; set; }
}
