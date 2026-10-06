using Microsoft.AspNetCore.Mvc;

namespace API_Thanh_toan.Models;

public class TokenRequest
{
    [FromForm(Name = "grant_type")]
    public string GrantType { get; set; } = string.Empty;
}
