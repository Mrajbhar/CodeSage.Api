using CodeSage.Api.Data;
using CodeSage.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;

namespace CodeSage.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/notifications")]
public class NotificationsController : ControllerBase
{
    private readonly MongoContext _db;
    private readonly OrgContext _org;
    public NotificationsController(MongoContext db, OrgContext org) { _db = db; _org = org; }

    // Recent notifications for the active org, newest first.
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] int limit = 30, [FromQuery] int offset = 0)
    {
        var (orgId, err) = await _org.ResolveAsync();
        if (err is not null) return err;

        offset = Math.Max(0, offset);
        limit = Math.Clamp(limit, 1, 100);

        var filter = Builders<Models.Notification>.Filter.Eq(n => n.OrgId, orgId);
        var total = await _db.Notifications.CountDocumentsAsync(filter);
        var items = await _db.Notifications.Find(filter)
            .SortByDescending(n => n.CreatedAt)
            .Skip(offset).Limit(limit)
            .ToListAsync();

        var unread = await _db.Notifications.CountDocumentsAsync(n => n.OrgId == orgId && !n.Read);

        return Ok(new
        {
            unread,
            total,
            hasMore = offset + items.Count < total,
            items = items.Select(n => new { id = n.Id, type = n.Type, message = n.Message, at = n.CreatedAt, read = n.Read })
        });
    }

    // Mark everything in the active org as read.
    [HttpPost("read")]
    public async Task<IActionResult> MarkRead()
    {
        var (orgId, err) = await _org.ResolveAsync();
        if (err is not null) return err;
        await _db.Notifications.UpdateManyAsync(
            n => n.OrgId == orgId && !n.Read,
            Builders<Models.Notification>.Update.Set(n => n.Read, true));
        return Ok(new { ok = true });
    }
}