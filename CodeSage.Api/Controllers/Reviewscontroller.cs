using CodeSage.Api.Data;
using CodeSage.Api.Dtos;
using CodeSage.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;

namespace CodeSage.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/reviews")]
public class ReviewsController : ControllerBase
{
    private readonly MongoContext _db;
    private readonly OrgContext _org;

    public ReviewsController(MongoContext db, OrgContext org)
    {
        _db = db; _org = org;
    }

    [HttpGet]
    public async Task<IActionResult> Recent([FromQuery] int limit = 8)
    {
        var (orgId, err) = await _org.ResolveAsync();
        if (err is not null) return err;

        var reviews = await _db.Reviews
            .Find(r => r.OrgId == orgId)
            .SortByDescending(r => r.CreatedAt)
            .Limit(Math.Clamp(limit, 1, 50))
            .ToListAsync();

        return Ok(reviews.Select(r => new ReviewSummaryDto(
            r.Id!, r.RepoFullName, r.PullNumber, r.Title, r.Summary,
            r.CommentCount, r.CriticalCount, r.RanByName, r.CreatedAt)));
    }
}