using System.Security.Claims;
using CodeSage.Api.Data;
using CodeSage.Api.Dtos;
using CodeSage.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;

namespace CodeSage.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/ai")]
public class AiController : ControllerBase
{
    private readonly AiService _ai;
    private readonly GitHubService _github;
    private readonly MongoContext _db;
    private readonly OrgContext _org;
    private readonly UsageService _usage;

    public AiController(AiService ai, GitHubService github, MongoContext db, OrgContext org, UsageService usage)
    {
        _ai = ai; _github = github; _db = db; _org = org; _usage = usage;
    }

    [HttpPost("explain")]
    public async Task<IActionResult> Explain(ExplainRequest req)
    {
        var (orgId, err) = await _org.ResolveAsync();
        if (err is not null) return err;
        if (string.IsNullOrWhiteSpace(req.Code))
            return BadRequest(new { message = "No code provided." });
        if (!_ai.IsConfigured)
            return StatusCode(503, new { message = "AI is not configured. Set an API key (or point at a local model) in settings." });

        var (ok, used, limit) = await _usage.CheckAsync(orgId!);
        if (!ok)
            return StatusCode(429, new { message = $"This month's AI quota is used up ({used} of {limit}). Upgrade the plan to keep reviewing." });

        var code = req.Code.Length > 12_000 ? req.Code[..12_000] : req.Code;
        try
        {
            var explanation = await _ai.ExplainCodeAsync(code, req.Path, req.Language);
            await _usage.IncrementAsync(orgId!, "explain");
            return Ok(new ExplainResponse(explanation));
        }
        catch (Exception ex)
        {
            return StatusCode(502, new { message = "The AI request failed. " + ex.Message });
        }
    }

    [HttpPost("review")]
    public async Task<IActionResult> Review(ReviewRequest req)
    {
        var (orgId, err) = await _org.ResolveAsync();
        if (err is not null) return err;
        if (!_ai.IsConfigured)
            return StatusCode(503, new { message = "AI is not configured. Set an API key (or point at a local model) in settings." });

        var (ok, used, limit) = await _usage.CheckAsync(orgId!);
        if (!ok)
            return StatusCode(429, new { message = $"This month's AI quota is used up ({used} of {limit}). Upgrade the plan to keep reviewing." });

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        var user = await _db.Users.Find(u => u.Id == userId).FirstOrDefaultAsync();
        if (user is null) return NotFound();
        if (user.GitHubAccessToken is null)
            return Conflict(new { message = "GitHub is not connected." });

        try
        {
            var diff = await _github.GetPullDiffAsync(user.GitHubAccessToken, req.FullName, req.Number);
            if (string.IsNullOrWhiteSpace(diff))
                return Ok(new ReviewResultDto("This pull request has no diff to review.", new()));

            var result = await _ai.ReviewDiffAsync(diff, $"{req.FullName} #{req.Number}");
            await _usage.IncrementAsync(orgId!, "review");
            return Ok(result);
        }
        catch (Exception ex)
        {
            return StatusCode(502, new { message = "Couldn't analyse the pull request. " + ex.Message });
        }
    }
}