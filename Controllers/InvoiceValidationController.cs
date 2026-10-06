using API_Thanh_toan.Models;
using API_Thanh_toan.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API_Thanh_toan.Controllers;

[Authorize]
[ApiController]
[Route("integration/v1/invoices")]
public class InvoiceValidationController : ControllerBase
{
    private readonly IInvoiceValidationService _validationService;
    private readonly ILogger<InvoiceValidationController> _logger;

    public InvoiceValidationController(
        IInvoiceValidationService validationService,
        ILogger<InvoiceValidationController> logger)
    {
        _validationService = validationService;
        _logger = logger;
    }

    [HttpPost("validate")]
    public async Task<IActionResult> ValidateInvoice([FromBody] ValidateInvoiceRequest request)
    {
        // 1. Input validation (returns 400 if properties are missing or malformed)
        if (!ModelState.IsValid || request.CustomerCode == null || request.InvoiceCode == null)
        {
            _logger.LogWarning("Validation failed: Invalid or missing request arguments.");
            return BadRequest(new { error = "invalid_request" });
        }

        // 2. Get authenticated Client ID from JWT claims
        var clientIdClaim = User.FindFirst("client_id") ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier);
        var clientId = clientIdClaim?.Value ?? "unknown";

        // 3. Perform client rate limiting (30 requests/minute)
        if (_validationService.IsRateLimited(clientId))
        {
            _logger.LogWarning("Validation API blocked: Client '{ClientId}' exceeded rate limit.", clientId);
            return StatusCode(StatusCodes.Status429TooManyRequests, new { error = "slow_down" });
        }

        // 4. Perform business rule validations
        var response = await _validationService.ValidateInvoiceAsync(request.CustomerCode.Value, request.InvoiceCode.Value);

        // 5. Audit Logging (Only logs non-sensitive details)
        if (response.IsValid)
        {
            _logger.LogInformation("Invoice validation successful: Client '{ClientId}' verified Invoice '{InvoiceCode}' for Customer '{CustomerCode}'.", 
                clientId, request.InvoiceCode.Value, request.CustomerCode.Value);
        }
        else
        {
            _logger.LogWarning("Invoice validation failed: Client '{ClientId}' failed validating Invoice '{InvoiceCode}' for Customer '{CustomerCode}'. Reason: '{Reason}'.", 
                clientId, request.InvoiceCode.Value, request.CustomerCode.Value, response.Reason);
        }

        return Ok(response);
    }
}
