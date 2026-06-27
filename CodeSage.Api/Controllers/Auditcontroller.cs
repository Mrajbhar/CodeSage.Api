using CodeSage.Api.Data;
using CodeSage.Api.Dtos;
using CodeSage.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;

namespace CodeSage.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/audit")]
public class AuditController : ControllerBase
{
    private readonly MongoContext _db;
    private readonly OrgContext _org;
    public AuditController(MongoContext db, OrgContext org) { _db = db; _org = org; }

    [HttpGet]
    public async Task<IActionResult> Recent([FromQuery] int limit = 50)
    {
        var (orgId, err) = await _org.ResolveAsync();
        if (err is not null) return err;

        var logs = await _db.AuditLogs
            .Find(a => a.OrgId == orgId)
            .SortByDescending(a => a.CreatedAt)
            .Limit(Math.Clamp(limit, 1, 200))
            .ToListAsync();

        return Ok(logs.Select(a => new AuditDto(a.Id!, a.ActorName, a.Action, a.Target, a.CreatedAt)));
    }

    // Paged variant: returns { items, total, hasMore } for "load more" / page controls.
    [HttpGet("paged")]
    public async Task<IActionResult> Paged([FromQuery] int offset = 0, [FromQuery] int limit = 25, [FromQuery] string? q = null)
    {
        var (orgId, err) = await _org.ResolveAsync();
        if (err is not null) return err;

        offset = Math.Max(0, offset);
        limit = Math.Clamp(limit, 1, 100);

        var b = Builders<Models.AuditLog>.Filter;
        var filter = b.Eq(a => a.OrgId, orgId);

        // Server-side search across actor / action / target (case-insensitive).
        if (!string.IsNullOrWhiteSpace(q))
        {
            var rx = new MongoDB.Bson.BsonRegularExpression(System.Text.RegularExpressions.Regex.Escape(q.Trim()), "i");
            filter &= b.Or(
                b.Regex(a => a.ActorName, rx),
                b.Regex(a => a.Action, rx),
                b.Regex(a => a.Target, rx));
        }

        var total = await _db.AuditLogs.CountDocumentsAsync(filter);
        var logs = await _db.AuditLogs.Find(filter)
            .SortByDescending(a => a.CreatedAt)
            .Skip(offset).Limit(limit)
            .ToListAsync();

        return Ok(new
        {
            total,
            items = logs.Select(a => new AuditDto(a.Id!, a.ActorName, a.Action, a.Target, a.CreatedAt)),
            hasMore = offset + logs.Count < total
        });
    }
}