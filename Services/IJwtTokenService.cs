namespace API_Thanh_toan.Services;

public interface IJwtTokenService
{
    string GenerateToken(string clientId);
    int GetTokenExpiryInSeconds();
}
