using API_Thanh_toan.Infrastructure;
using API_Thanh_toan.Models;
using API_Thanh_toan.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API_Thanh_toan.Controllers;

[Authorize]
[ServiceFilter(typeof(HmacSignatureFilter))]
[ApiController]
[Route("integration/v1")]
public class PaymentController : ControllerBase
{
    private readonly IPaymentService _paymentService;
    private readonly ILogger<PaymentController> _logger;

    public PaymentController(
        IPaymentService paymentService,
        ILogger<PaymentController> logger)
    {
        _paymentService = paymentService;
        _logger = logger;
    }

    [HttpPost("payments")]
    public async Task<IActionResult> ProcessPayment([FromBody] PaymentRequest request)
    {
        // 1. Model State Validation
        if (!ModelState.IsValid || 
            request.CustomerCode == null || 
            request.TotalAmount == null || 
            string.IsNullOrWhiteSpace(request.PartnerTransactionId))
        {
            _logger.LogWarning("Payment failed: Invalid or missing request parameters.");
            return BadRequest(new { error = "invalid_request" });
        }

        // 2. Get client_id from JWT claims
        var clientIdClaim = User.FindFirst("client_id") ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier);
        var clientId = clientIdClaim?.Value ?? "unknown";

        // 3. Check rate limit (20 req/min)
        if (_paymentService.IsRateLimited(clientId))
        {
            _logger.LogWarning("Payment API blocked: Client '{ClientId}' exceeded rate limit.", clientId);
            return StatusCode(StatusCodes.Status429TooManyRequests, new { error = "slow_down" });
        }

        // 4. Process payment transaction (Steps 2 - 7)
        var (statusCode, response, systemError) = await _paymentService.ProcessPaymentAsync(clientId, request);

        if (systemError != null)
        {
            if (statusCode == StatusCodes.Status403Forbidden)
            {
                return StatusCode(StatusCodes.Status403Forbidden, new { error = systemError });
            }
            if (statusCode == StatusCodes.Status409Conflict)
            {
                return StatusCode(StatusCodes.Status409Conflict, new { error = systemError });
            }
            return StatusCode(statusCode, new { error = systemError });
        }

        return StatusCode(statusCode, response);
    }
}
