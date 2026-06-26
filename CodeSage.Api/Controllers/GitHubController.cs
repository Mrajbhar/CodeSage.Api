using System.Security.Claims;
using CodeSage.Api.Data;
using CodeSage.Api.Dtos;
using CodeSage.Api.Models;
using CodeSage.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;

namespace CodeSage.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/github")]
public class GitHubController : ControllerBase
{
    private readonly MongoContext _db;
    private readonly GitHubService _github;
    private readonly CacheService _cache;
    private readonly OrgContext _org;

    public GitHubController(MongoContext db, GitHubService github, CacheService cache, OrgContext org)
    {
        _db = db; _github = github; _cache = cache; _org = org;
    }

    // ---- Automated PR review enrolment ----
    [HttpGet("watched")]
    public async Task<IActionResult> Watched()
    {
        var (orgId, err) = await _org.ResolveAsync();
        if (err is not null) return err;
        var list = await _db.WatchedRepos.Find(w => w.OrgId == orgId).ToListAsync();
        return Ok(list.Select(w => w.RepoFullName));
    }

    [HttpPost("watch")]
    public async Task<IActionResult> Watch([FromBody] WatchRequest req)
    {
        var (orgId, err) = await _org.ResolveAsync();
        if (err is not null) return err;
        if (string.IsNullOrWhiteSpace(req.FullName)) return BadRequest(new { message = "Repo is required." });

        var exists = await _db.WatchedRepos.Find(w => w.OrgId == orgId && w.RepoFullName == req.FullName).AnyAsync();
        if (!exists)
            await _db.WatchedRepos.InsertOneAsync(new Models.WatchedRepo
            { OrgId = orgId!, RepoFullName = req.FullName, UserId = Uid()! });
        return Ok(new { watching = true });
    }

    [HttpDelete("watch")]
    public async Task<IActionResult> Unwatch([FromQuery] string fullName)
    {
        var (orgId, err) = await _org.ResolveAsync();
        if (err is not null) return err;
        await _db.WatchedRepos.DeleteManyAsync(w => w.OrgId == orgId && w.RepoFullName == fullName);
        return Ok(new { watching = false });
    }

    [HttpGet("repos")]
    public Task<IActionResult> Repos() =>
        WithToken(token => _cache.GetOrSetAsync($"repos:{Uid()}", TimeSpan.FromSeconds(60),
            () => _github.GetReposAsync(token)));

    [HttpGet("overview")]
    public Task<IActionResult> Overview() =>
        WithToken(token => _cache.GetOrSetAsync($"overview:{Uid()}", TimeSpan.FromSeconds(60),
            () => _github.GetOverviewAsync(token)));

    [HttpGet("tree")]
    public Task<IActionResult> Tree([FromQuery] string fullName) =>
        WithToken(token => _cache.GetOrSetAsync($"tree:{fullName}", TimeSpan.FromSeconds(300),
            () => _github.GetTreeAsync(token, fullName)));

    [HttpGet("file")]
    public Task<IActionResult> File([FromQuery] string fullName, [FromQuery] string path) =>
        WithToken(token => _github.GetFileContentAsync(token, fullName, path));

    [HttpGet("pulls")]
    public Task<IActionResult> Pulls([FromQuery] string fullName) =>
        WithToken(token => _github.GetPullsAsync(token, fullName));

    private string? Uid() => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");

    // Resolves the caller's stored GitHub token, runs the work, and maps failures.
    private async Task<IActionResult> WithToken<T>(Func<string, Task<T>> work)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        var user = await _db.Users.Find(u => u.Id == userId).FirstOrDefaultAsync();
        if (user is null) return NotFound();
        if (user.GitHubAccessToken is null)
            return Conflict(new { message = "GitHub is not connected." });

        try
        {
            return Ok(await work(user.GitHubAccessToken));
        }
        catch
        {
            return StatusCode(502, new { message = "Couldn't reach GitHub. Try reconnecting your account." });
        }
    }
}