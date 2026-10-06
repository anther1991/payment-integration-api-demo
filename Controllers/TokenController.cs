using API_Thanh_toan.Models;
using API_Thanh_toan.Services;
using Microsoft.AspNetCore.Mvc;

namespace API_Thanh_toan.Controllers;

[ApiController]
[Route("integration/v1/oauth")]
public class TokenController : ControllerBase
{
    private readonly IClientAuthService _authService;
    private readonly IJwtTokenService _jwtService;
    private readonly ILogger<TokenController> _logger;

    public TokenController(
        IClientAuthService authService,
        IJwtTokenService jwtService,
        ILogger<TokenController> logger)
    {
        _authService = authService;
        _jwtService = jwtService;
        _logger = logger;
    }

    [HttpPost("token")]
    [Consumes("application/x-www-form-urlencoded")]
    public async Task<IActionResult> GetToken([FromForm] TokenRequest request)
    {
        // 1. Validate form fields
        if (request == null || string.IsNullOrWhiteSpace(request.GrantType))
        {
            _logger.LogWarning("Authentication failed: Missing grant_type.");
            return BadRequest(new { error = "invalid_request" });
        }

        if (request.GrantType != "client_credentials")
        {
            _logger.LogWarning("Authentication failed: Unsupported grant_type '{GrantType}'.", request.GrantType);
            return BadRequest(new { error = "invalid_request" });
        }

        // 2. Parse Basic Auth Header
        var authHeader = Request.Headers.Authorization.ToString();
        if (string.IsNullOrWhiteSpace(authHeader))
        {
            _logger.LogWarning("Authentication failed: Missing Authorization header.");
            return BadRequest(new { error = "invalid_request" });
        }

        var (clientId, clientSecret) = _authService.ParseBasicAuthHeader(authHeader);
        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
        {
            _logger.LogWarning("Authentication failed: Malformed Basic Authorization header.");
            return BadRequest(new { error = "invalid_request" });
        }

        // 3. Extract source IP
        var sourceIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        // 4. Check rate limiting per IP + ClientId pair
        if (_authService.IsRateLimited(clientId, sourceIp))
        {
            _logger.LogWarning("Authentication blocked: Client '{ClientId}' from IP '{SourceIp}' is rate limited.", clientId, sourceIp);
            return StatusCode(StatusCodes.Status429TooManyRequests, new { error = "slow_down" });
        }

        // 5. Authenticate client credentials
        var isAuthenticated = await _authService.AuthenticateAsync(clientId, clientSecret);
        if (!isAuthenticated)
        {
            _authService.IncrementFailedAttempts(clientId, sourceIp);
            _logger.LogWarning("Authentication failed: Invalid credentials for Client '{ClientId}' from IP '{SourceIp}'.", clientId, sourceIp);
            return Unauthorized(new { error = "invalid_client" });
        }

        // 6. Generate JWT Token on success
        var token = _jwtService.GenerateToken(clientId);
        var expiresIn = _jwtService.GetTokenExpiryInSeconds();

        _logger.LogInformation("Authentication successful for Client '{ClientId}' from IP '{SourceIp}'. Token generated.", clientId, sourceIp);

        return Ok(new TokenResponse
        {
            AccessToken = token,
            ExpiresIn = expiresIn,
            TokenType = "Bearer"
        });
    }
}
