using System.Security.Cryptography;
using System.Text;
using API_Thanh_toan.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace API_Thanh_toan.Infrastructure;

public class HmacSignatureFilter : IAsyncActionFilter
{
    private readonly IPaymentClientRepository _clientRepository;
    private readonly IDataProtectionHelper _protectionHelper;
    private readonly ILogger<HmacSignatureFilter> _logger;

    public HmacSignatureFilter(
        IPaymentClientRepository clientRepository,
        IDataProtectionHelper protectionHelper,
        ILogger<HmacSignatureFilter> logger)
    {
        _clientRepository = clientRepository;
        _protectionHelper = protectionHelper;
        _logger = logger;
    }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var httpContext = context.HttpContext;

        // 1. Check X-Timestamp header
        if (!httpContext.Request.Headers.TryGetValue("X-Timestamp", out var timestampValues) ||
            !long.TryParse(timestampValues.ToString(), out var clientTimestamp))
        {
            _logger.LogWarning("HMAC verification failed: Missing or invalid X-Timestamp header.");
            context.Result = new UnauthorizedObjectResult(new { error = "invalid_signature" });
            return;
        }

        var currentServerTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (Math.Abs(currentServerTime - clientTimestamp) > 300) // 5 minutes tolerance
        {
            _logger.LogWarning("HMAC verification failed: X-Timestamp expired. Server: {ServerTime}, Client: {ClientTime}", currentServerTime, clientTimestamp);
            context.Result = new UnauthorizedObjectResult(new { error = "timestamp_expired" });
            return;
        }

        // 2. Check X-Signature header
        if (!httpContext.Request.Headers.TryGetValue("X-Signature", out var signatureValues) ||
            string.IsNullOrWhiteSpace(signatureValues.ToString()))
        {
            _logger.LogWarning("HMAC verification failed: Missing X-Signature header.");
            context.Result = new UnauthorizedObjectResult(new { error = "invalid_signature" });
            return;
        }

        var receivedSignature = signatureValues.ToString().Trim();

        // 3. Get client_id from JWT
        var clientIdClaim = httpContext.User.FindFirst("client_id") ?? httpContext.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier);
        var clientId = clientIdClaim?.Value;

        if (string.IsNullOrWhiteSpace(clientId))
        {
            _logger.LogWarning("HMAC verification failed: Missing client_id claim in JWT.");
            context.Result = new UnauthorizedObjectResult(new { error = "invalid_signature" });
            return;
        }

        // 4. Fetch Client from Database
        var client = await _clientRepository.GetByIdAsync(clientId);
        if (client == null || !client.IsActive)
        {
            _logger.LogWarning("HMAC verification failed: Client '{ClientId}' not found or inactive.", clientId);
            context.Result = new UnauthorizedObjectResult(new { error = "invalid_signature" });
            return;
        }

        // 5. Decrypt ClientSecretEncrypted or fallback if null
        string plainSecret = string.Empty;
        if (client.ClientSecretEncrypted != null && client.ClientSecretEncrypted.Length > 0)
        {
            plainSecret = _protectionHelper.DecryptSecret(client.ClientSecretEncrypted);
        }

        if (string.IsNullOrEmpty(plainSecret))
        {
            _logger.LogWarning("HMAC verification failed: ClientSecretEncrypted is empty for Client '{ClientId}'.", clientId);
            context.Result = new UnauthorizedObjectResult(new { error = "invalid_signature" });
            return;
        }

        // 6. Read Raw Body
        httpContext.Request.Body.Position = 0;
        using var reader = new StreamReader(httpContext.Request.Body, Encoding.UTF8, leaveOpen: true);
        var rawBody = await reader.ReadToEndAsync();
        httpContext.Request.Body.Position = 0;

        // 7. Construct stringToSign = method + "\n" + path + "\n" + X-Timestamp + "\n" + rawBody
        var method = httpContext.Request.Method.ToUpperInvariant();
        var path = httpContext.Request.Path.Value ?? string.Empty;
        var stringToSign = $"{method}\n{path}\n{clientTimestamp}\n{rawBody}";

        // 8. Compute HMAC-SHA256
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(plainSecret));
        var computedHashBytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(stringToSign));
        var expectedSignatureHex = Convert.ToHexString(computedHashBytes).ToLowerInvariant();

        // 9. Constant-time comparison
        var receivedBytes = Encoding.UTF8.GetBytes(receivedSignature.ToLowerInvariant());
        var expectedBytes = Encoding.UTF8.GetBytes(expectedSignatureHex);

        if (receivedBytes.Length != expectedBytes.Length || !CryptographicOperations.FixedTimeEquals(receivedBytes, expectedBytes))
        {
            _logger.LogWarning("HMAC verification failed: Signature mismatch for Client '{ClientId}'.", clientId);
            context.Result = new UnauthorizedObjectResult(new { error = "invalid_signature" });
            return;
        }

        // Signature verified successfully
        await next();
    }
}
