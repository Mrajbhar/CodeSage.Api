using System.Text.Json;
using CodeSage.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CodeSage.Api.Controllers;

[ApiController]
[AllowAnonymous]
[Route("api/billing/webhook")]
public class RazorpayWebhookController : ControllerBase
{
    private readonly RazorpayClient _rzp;
    private readonly BillingService _billing;
    private readonly ILogger<RazorpayWebhookController> _log;

    public RazorpayWebhookController(RazorpayClient rzp, BillingService billing, ILogger<RazorpayWebhookController> log)
    {
        _rzp = rzp; _billing = billing; _log = log;
    }

    [HttpPost]
    public async Task<IActionResult> Receive()
    {
        // Razorpay needs the EXACT raw body to verify the HMAC signature, so we read it ourselves.
        using var reader = new StreamReader(Request.Body);
        var body = await reader.ReadToEndAsync();
        var signature = Request.Headers["X-Razorpay-Signature"].ToString();

        if (!_rzp.VerifyWebhookSignature(body, signature))
        {
            _log.LogWarning("Razorpay webhook signature failed");
            return Unauthorized();
        }

        try
        {
            using var doc = JsonDocument.Parse(body);
            var eventType = doc.RootElement.GetProperty("event").GetString() ?? "";
            var entity = doc.RootElement.GetProperty("payload").GetProperty("subscription").GetProperty("entity");

            var subId = entity.GetProperty("id").GetString() ?? "";
            var status = entity.GetProperty("status").GetString() ?? "";
            // For subscription.charged the amount is on the payment entity, not the subscription.
            int amount = 0;
            if (doc.RootElement.GetProperty("payload").TryGetProperty("payment", out var payment) &&
                payment.TryGetProperty("entity", out var pe) &&
                pe.TryGetProperty("amount", out var amt))
                amount = amt.GetInt32();

            await _billing.HandleWebhookEventAsync(eventType, subId, status, amount);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Razorpay webhook handling failed");
            // Still return 200 so Razorpay doesn't retry forever on malformed events.
        }
        return Ok();
    }
}