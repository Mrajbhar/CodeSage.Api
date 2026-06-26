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
}