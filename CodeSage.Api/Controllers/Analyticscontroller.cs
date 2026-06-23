using System.Security.Claims;
using CodeSage.Api.Data;
using CodeSage.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;

namespace CodeSage.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/analytics")]
public class AnalyticsController : ControllerBase
{
    private readonly MongoContext _db;
    private readonly UsageService _usage;
    public AnalyticsController(MongoContext db, UsageService usage) { _db = db; _usage = usage; }

    [HttpGet("overview")]
    public async Task<IActionResult> Overview([FromQuery] string orgId)
    {
        var uid = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        var org = await _db.Organizations.Find(o => o.Id == orgId).FirstOrDefaultAsync();
        if (org is null || !org.Members.Any(m => m.UserId == uid)) return Forbid();

        var current = await _usage.GetCurrentAsync(orgId);
        var history = await _usage.GetHistoryAsync(orgId);
        var commentCount = await _db.Comments.CountDocumentsAsync(FilterDefinition<Models.Comment>.Empty);

        return Ok(new
        {
            members = org.Members.Count,
            plan = org.Plan,
            usage = current,
            history,
            comments = commentCount
        });
    }
}