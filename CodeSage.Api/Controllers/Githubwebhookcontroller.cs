using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CodeSage.Api.Data;
using CodeSage.Api.Jobs;
using CodeSage.Api.Settings;
using Hangfire;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace CodeSage.Api.Controllers;

[ApiController]
[AllowAnonymous]
[Route("api/github/webhook")]
public class GitHubWebhookController : ControllerBase
{
    private readonly MongoContext _db;
    private readonly OAuthSettings _oauth;
    private readonly ILogger<GitHubWebhookController> _log;

    public GitHubWebhookController(MongoContext db, IOptions<OAuthSettings> oauth, ILogger<GitHubWebhookController> log)
    {
        _db = db; _oauth = oauth.Value; _log = log;
    }

    [HttpPost]
    public async Task<IActionResult> Receive()
    {
        using var reader = new StreamReader(Request.Body);
        var payload = await reader.ReadToEndAsync();

        if (!VerifySignature(payload, Request.Headers["X-Hub-Signature-256"].ToString()))
            return Unauthorized();

        var eventType = Request.Headers["X-GitHub-Event"].ToString();
        if (eventType != "pull_request") return Ok();   // ignore everything else

        try
        {
            using var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement;
            var action = root.GetProperty("action").GetString();
            if (action is not ("opened" or "reopened" or "synchronize")) return Ok();

            var pr = root.GetProperty("pull_request");
            var number = pr.GetProperty("number").GetInt32();
            var title = pr.GetProperty("title").GetString() ?? "";
            var fullName = root.GetProperty("repository").GetProperty("full_name").GetString()!;

            var watch = await _db.WatchedRepos.Find(w => w.RepoFullName == fullName).FirstOrDefaultAsync();
            if (watch is null) return Ok();   // repo not enrolled for auto-review

            BackgroundJob.Enqueue<BackgroundJobs>(j => j.AutoReviewPr(watch.OrgId, watch.UserId, fullName, number, title));
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "GitHub webhook handling failed");
        }
        return Ok();
    }

    private bool VerifySignature(string payload, string signatureHeader)
    {
        var secret = _oauth.GitHub.WebhookSecret;
        if (string.IsNullOrWhiteSpace(secret) || string.IsNullOrWhiteSpace(signatureHeader)) return false;
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = "sha256=" + Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
        return CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(hash), Encoding.ASCII.GetBytes(signatureHeader));
    }
}