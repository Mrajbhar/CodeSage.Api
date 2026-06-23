using System.Security.Claims;
using CodeSage.Api.Data;
using CodeSage.Api.Dtos;
using CodeSage.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;

namespace CodeSage.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/billing")]
public class BillingController : ControllerBase
{
    private readonly MongoContext _db;
    private readonly BillingService _billing;
    private readonly UsageService _usage;
    private readonly string _rzpKeyId;

    public BillingController(MongoContext db, BillingService billing, UsageService usage, RazorpayClient rzp)
    {
        _db = db; _billing = billing; _usage = usage; _rzpKeyId = rzp.KeyId;
    }


    [HttpGet("config")]
    [AllowAnonymous]
    public IActionResult Config() => Ok(new
    {
        keyId = _rzpKeyId,
        live = _billing.IsLive
    });
    [HttpGet("plans")]
    public IActionResult Plans() => Ok(PlanCatalog.All.Select(p =>
        new PlanDto(p.Id, p.Name, p.PriceCents, p.AiCallsPerMonth, p.MemberLimit, p.Features)));

    [HttpGet("summary")]
    public async Task<IActionResult> Summary([FromQuery] string orgId)
    {
        if (!await IsMemberAsync(orgId)) return Forbid();
        return Ok(await _billing.GetSummaryAsync(orgId));
    }

    [HttpGet("usage")]
    public async Task<IActionResult> Usage([FromQuery] string orgId)
    {
        if (!await IsMemberAsync(orgId)) return Forbid();
        return Ok(await _usage.GetCurrentAsync(orgId));
    }

    [HttpPost("checkout")]
    public async Task<IActionResult> Checkout([FromQuery] string orgId, CheckoutRequest req)
    {
        if (!await CanManageAsync(orgId)) return Forbid();
        var result = await _billing.CheckoutAsync(orgId, req.Plan);
        return Ok(result);
    }
    private async Task<bool> IsMemberAsync(string orgId)
    {
        var uid = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        var org = await _db.Organizations.Find(o => o.Id == orgId).FirstOrDefaultAsync();
        return org is not null && org.Members.Any(m => m.UserId == uid);
    }
    private async Task<bool> CanManageAsync(string orgId)
    {
        var uid = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        var org = await _db.Organizations.Find(o => o.Id == orgId).FirstOrDefaultAsync();
        var me = org?.Members.FirstOrDefault(m => m.UserId == uid);
        return me?.Role is "Owner" or "Admin";
    }
}