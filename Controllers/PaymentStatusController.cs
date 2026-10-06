using API_Thanh_toan.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API_Thanh_toan.Controllers;

[Authorize]
[ApiController]
[Route("integration/v1")]
public class PaymentStatusController : ControllerBase
{
    private readonly IPaymentStatusService _statusService;
    private readonly ILogger<PaymentStatusController> _logger;

    public PaymentStatusController(
        IPaymentStatusService statusService,
        ILogger<PaymentStatusController> logger)
    {
        _statusService = statusService;
        _logger = logger;
    }

    [HttpGet("payments/{partnerTransactionId}")]
    public async Task<IActionResult> GetPaymentStatus(string partnerTransactionId)
    {
        // 1. Get authenticated Client ID from JWT claims
        var clientIdClaim = User.FindFirst("client_id") ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier);
        var clientId = clientIdClaim?.Value ?? "unknown";

        // 2. Perform client rate limiting (30 requests/minute)
        if (_statusService.IsRateLimited(clientId))
        {
            _logger.LogWarning("Payment status inquiry blocked: Client '{ClientId}' exceeded rate limit.", clientId);
            return StatusCode(StatusCodes.Status429TooManyRequests, new { error = "slow_down" });
        }

        // 3. Inquire status
        var (statusCode, response, errorCode) = await _statusService.InquirePaymentStatusAsync(clientId, partnerTransactionId);

        if (errorCode != null)
        {
            _logger.LogWarning("Payment status inquiry failed for PartnerTransactionId '{PartnerTxId}' by Client '{ClientId}'. Reason: '{ErrorCode}'.",
                partnerTransactionId, clientId, errorCode);
            return StatusCode(statusCode, new { error = errorCode });
        }

        _logger.LogInformation("Payment status inquiry successful for PartnerTransactionId '{PartnerTxId}' by Client '{ClientId}'. Status: '{Status}'.",
            partnerTransactionId, clientId, response?.Status);

        return Ok(response);
    }
}
