using System.Security.Claims;
using CodeSage.Api.Data;
using CodeSage.Api.Dtos;
using CodeSage.Api.Jobs;
using CodeSage.Api.Services;
using Hangfire;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;

namespace CodeSage.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/search")]
public class SearchController : ControllerBase
{
    private readonly MongoContext _db;
    private readonly OrgContext _org;
    private readonly IndexService _index;

    public SearchController(MongoContext db, OrgContext org, IndexService index)
    {
        _db = db; _org = org; _index = index;
    }

    // Kick off indexing in the background (Hangfire job), return immediately.
    [HttpPost("index")]
    public async Task<IActionResult> Index(IndexRequest req)
    {
        var (orgId, err) = await _org.ResolveAsync();
        if (err is not null) return err;

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        var user = await _db.Users.Find(u => u.Id == userId).FirstOrDefaultAsync();
        if (user?.GitHubAccessToken is null) return Conflict(new { message = "GitHub is not connected." });

        BackgroundJob.Enqueue<BackgroundJobs>(j => j.IndexRepo(orgId!, user.GitHubAccessToken, req.FullName));
        return Accepted(new { message = $"Indexing {req.FullName} started. You'll be notified when it's ready." });
    }

    // Semantic search over the indexed vectors for a repo.
    [HttpGet]
    public async Task<IActionResult> Search([FromQuery] string fullName, [FromQuery] string q)
    {
        var (orgId, err) = await _org.ResolveAsync();
        if (err is not null) return err;
        if (string.IsNullOrWhiteSpace(q)) return Ok(Array.Empty<SearchResultDto>());

        try
        {
            var results = await _index.SearchAsync(orgId!, fullName, q);
            return Ok(results);
        }
        catch (Exception ex)
        {
            return StatusCode(502, new { message = "Search failed (is the embedding model running?). " + ex.Message });
        }
    }
}