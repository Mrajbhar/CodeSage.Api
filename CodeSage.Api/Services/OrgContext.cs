using System.Security.Claims;
using CodeSage.Api.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;

namespace CodeSage.Api.Services;

public class OrgContext
{
    private readonly IHttpContextAccessor _http;
    private readonly MongoContext _db;
    public OrgContext(IHttpContextAccessor http, MongoContext db) { _http = http; _db = db; }

    // Returns the org id from the X-Org-Id header, after confirming the caller is a member.
    public async Task<(string? OrgId, IActionResult? Error)> ResolveAsync()
    {
        var ctx = _http.HttpContext!;
        var orgId = ctx.Request.Headers["X-Org-Id"].ToString();
        if (string.IsNullOrWhiteSpace(orgId))
            return (null, new BadRequestObjectResult(new { message = "Missing X-Org-Id header — pick an organization." }));

        var uid = ctx.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? ctx.User.FindFirstValue("sub");
        var org = await _db.Organizations.Find(o => o.Id == orgId).FirstOrDefaultAsync();
        if (org is null || !org.Members.Any(m => m.UserId == uid))
            return (null, new ForbidResult());

        return (orgId, null);
    }
}