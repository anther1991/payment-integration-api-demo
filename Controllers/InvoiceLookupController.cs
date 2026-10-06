using API_Thanh_toan.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API_Thanh_toan.Controllers;

[Authorize]
[ApiController]
[Route("integration/v1")]
public class InvoiceLookupController : ControllerBase
{
    private readonly IInvoiceLookupService _lookupService;
    private readonly ILogger<InvoiceLookupController> _logger;

    public InvoiceLookupController(
        IInvoiceLookupService lookupService,
        ILogger<InvoiceLookupController> logger)
    {
        _lookupService = lookupService;
        _logger = logger;
    }

    [HttpGet("customers/{customerCode:int}/invoices")]
    public async Task<IActionResult> LookupInvoices(int customerCode)
    {
        // 1. Get authenticated Client ID from JWT claims
        var clientIdClaim = User.FindFirst("client_id") ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier);
        var clientId = clientIdClaim?.Value ?? "unknown";

        // 2. Perform client rate limiting (30 requests/minute)
        if (_lookupService.IsRateLimited(clientId))
        {
            _logger.LogWarning("Lookup API blocked: Client '{ClientId}' exceeded rate limit.", clientId);
            return StatusCode(StatusCodes.Status429TooManyRequests, new { error = "slow_down" });
        }

        // 3. Extract source IP
        var sourceIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        // 4. Sequential lookup detection
        _lookupService.DetectSequentialQueryPattern(clientId, customerCode, sourceIp);

        // 5. Query customer and invoices (2-step logic)
        var result = await _lookupService.LookupInvoicesAsync(customerCode);
        if (result == null)
        {
            // Only log ID, do not log sensitive data
            _logger.LogWarning("Lookup failed: Customer '{CustomerCode}' not found. Request by '{ClientId}' from IP '{SourceIp}'.", 
                customerCode, clientId, sourceIp);
            return NotFound(new { error = "customer_not_found" });
        }

        _logger.LogInformation("Lookup successful: Customer '{CustomerCode}'. Request by '{ClientId}' from IP '{SourceIp}'.", 
            customerCode, clientId, sourceIp);

        return Ok(result);
    }
}
