using System.Security.Claims;
using CodeSage.Api.Data;
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

    public GitHubController(MongoContext db, GitHubService github)
    {
        _db = db; _github = github;
    }

    [HttpGet("repos")]
    public Task<IActionResult> Repos() =>
        WithToken(token => _github.GetReposAsync(token));

    [HttpGet("overview")]
    public Task<IActionResult> Overview() =>
        WithToken(token => _github.GetOverviewAsync(token));

    [HttpGet("tree")]
    public Task<IActionResult> Tree([FromQuery] string fullName) =>
        WithToken(token => _github.GetTreeAsync(token, fullName));

    [HttpGet("file")]
    public Task<IActionResult> File([FromQuery] string fullName, [FromQuery] string path) =>
        WithToken(token => _github.GetFileContentAsync(token, fullName, path));

    [HttpGet("pulls")]
    public Task<IActionResult> Pulls([FromQuery] string fullName) =>
        WithToken(token => _github.GetPullsAsync(token, fullName));

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