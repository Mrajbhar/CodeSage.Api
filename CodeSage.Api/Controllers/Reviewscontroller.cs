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

    // Full detail for one review, including every finding.
    [HttpGet("{id}")]
    public async Task<IActionResult> Detail(string id)
    {
        var (orgId, err) = await _org.ResolveAsync();
        if (err is not null) return err;

        var r = await _db.Reviews.Find(x => x.Id == id && x.OrgId == orgId).FirstOrDefaultAsync();
        if (r is null) return NotFound(new { message = "Review not found." });

        return Ok(new ReviewDetailDto(
            r.Id!, r.RepoFullName, r.PullNumber, r.Title, r.Summary,
            r.CommentCount, r.CriticalCount, r.RanByName, r.CreatedAt,
            r.Comments.Select(c => new ReviewCommentDto(c.File, c.Severity, c.Comment)).ToList()));
    }
    [HttpGet("paged")]
    public async Task<IActionResult> Paged([FromQuery] int offset = 0, [FromQuery] int limit = 15)
    {
        var (orgId, err) = await _org.ResolveAsync();
        if (err is not null) return err;

        offset = Math.Max(0, offset);
        limit = Math.Clamp(limit, 1, 50);

        var filter = Builders<Models.Review>.Filter.Eq(r => r.OrgId, orgId);
        var total = await _db.Reviews.CountDocumentsAsync(filter);
        var reviews = await _db.Reviews.Find(filter)
            .SortByDescending(r => r.CreatedAt)
            .Skip(offset).Limit(limit)
            .ToListAsync();

        return Ok(new
        {
            total,
            items = reviews.Select(r => new ReviewSummaryDto(
                r.Id!, r.RepoFullName, r.PullNumber, r.Title, r.Summary,
                r.CommentCount, r.CriticalCount, r.RanByName, r.CreatedAt)),
            hasMore = offset + reviews.Count < total
        });
    }
}