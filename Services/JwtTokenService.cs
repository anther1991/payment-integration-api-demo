using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace API_Thanh_toan.Services;

public class JwtTokenService : IJwtTokenService
{
    private readonly string _secretKey;
    private readonly string _issuer;
    private readonly string _audience;
    private const int ExpiryMinutes = 15;

    public JwtTokenService(IConfiguration configuration)
    {
        _secretKey = configuration["JwtSettings:SecretKey"] 
            ?? throw new InvalidOperationException("JWT SecretKey is not configured.");
        _issuer = configuration["JwtSettings:Issuer"] 
            ?? throw new InvalidOperationException("JWT Issuer is not configured.");
        _audience = configuration["JwtSettings:Audience"] 
            ?? throw new InvalidOperationException("JWT Audience is not configured.");

        if (Encoding.UTF8.GetByteCount(_secretKey) < 32)
        {
            throw new InvalidOperationException("JWT SecretKey must be at least 256 bits (32 bytes) long.");
        }
    }

    public string GenerateToken(string clientId)
    {
        var securityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_secretKey));
        var signingCredentials = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);

        var tokenHandler = new JsonWebTokenHandler();
        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(new[]
            {
                new Claim(JwtRegisteredClaimNames.Sub, clientId),
                new Claim("client_id", clientId)
            }),
            Expires = DateTime.UtcNow.AddMinutes(ExpiryMinutes),
            Issuer = _issuer,
            Audience = _audience,
            SigningCredentials = signingCredentials
        };

        return tokenHandler.CreateToken(tokenDescriptor);
    }

    public int GetTokenExpiryInSeconds() => ExpiryMinutes * 60;
}
