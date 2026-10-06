namespace API_Thanh_toan.Services;

public interface IClientAuthService
{
    (string? clientId, string? clientSecret) ParseBasicAuthHeader(string authorizationHeader);
    Task<bool> AuthenticateAsync(string clientId, string clientSecret);
    bool IsRateLimited(string clientId, string sourceIp);
    void IncrementFailedAttempts(string clientId, string sourceIp);
}
