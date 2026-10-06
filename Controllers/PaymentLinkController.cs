using API_Thanh_toan.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API_Thanh_toan.Controllers;

[Authorize]
[ApiController]
[Route("integration/v1")]
public class PaymentLinkController : ControllerBase
{
    private readonly IPaymentLinkLookupService _paymentLinkService;
    private readonly ILogger<PaymentLinkController> _logger;

    public PaymentLinkController(
        IPaymentLinkLookupService paymentLinkService,
        ILogger<PaymentLinkController> logger)
    {
        _paymentLinkService = paymentLinkService;
        _logger = logger;
    }

    [HttpGet("payment-links/{token}/invoices")]
    public async Task<IActionResult> GetInvoicesByPaymentLinkToken(string token)
    {
        var clientIdClaim = User.FindFirst("client_id") ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier);
        var clientId = clientIdClaim?.Value ?? "unknown";

        if (_paymentLinkService.IsRateLimited(clientId))
        {
            _logger.LogWarning("Payment link lookup API blocked: Client '{ClientId}' exceeded rate limit.", clientId);
            return StatusCode(StatusCodes.Status429TooManyRequests, new { error = "slow_down" });
        }

        var (statusCode, response, errorCode) = await _paymentLinkService.GetInvoicesByTokenAsync(clientId, token);
        if (statusCode != StatusCodes.Status200OK)
        {
            return StatusCode(statusCode, new { error = errorCode });
        }

        return Ok(response);
    }
}
