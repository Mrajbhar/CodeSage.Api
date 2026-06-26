using CodeSage.Api.Data;
using CodeSage.Api.Services;
using MongoDB.Driver;

namespace CodeSage.Api.Jobs;

// Phase 4 #2: out-of-request work — repo indexing, automated PR review, periodic cleanup.
public class BackgroundJobs
{
    private readonly IndexService _index;
    private readonly NotificationService _notify;
    private readonly MongoContext _db;
    private readonly AiService _ai;
    private readonly GitHubService _github;
    private readonly UsageService _usage;

    public BackgroundJobs(IndexService index, NotificationService notify, MongoContext db,
        AiService ai, GitHubService github, UsageService usage)
    {
        _index = index; _notify = notify; _db = db; _ai = ai; _github = github; _usage = usage;
    }

    // Enqueued when a user asks to index a repo.
    public async Task IndexRepo(string orgId, string githubToken, string fullName)
    {
        await _index.IndexRepoAsync(orgId, githubToken, fullName);
        await _notify.SendToOrgAsync(orgId, "index.done", $"Semantic index ready for {fullName}");
    }

    // Automated PR review: triggered by the GitHub webhook when a PR opens/updates.
    public async Task AutoReviewPr(string orgId, string userId, string fullName, int number, string title)
    {
        var user = await _db.Users.Find(u => u.Id == userId).FirstOrDefaultAsync();
        if (user?.GitHubAccessToken is null) return;

        // Respect the org's monthly quota even for automated runs.
        var (ok, _, _) = await _usage.CheckAsync(orgId);
        if (!ok) { await _notify.SendToOrgAsync(orgId, "quota", $"Auto-review skipped for {fullName} #{number}: monthly quota reached."); return; }

        var diff = await _github.GetPullDiffAsync(user.GitHubAccessToken, fullName, number);
        if (string.IsNullOrWhiteSpace(diff)) return;

        var result = await _ai.ReviewDiffAsync(diff, $"{fullName} #{number}");
        await _usage.IncrementAsync(orgId, "review");

        await _db.Reviews.InsertOneAsync(new Models.Review
        {
            OrgId = orgId, RepoFullName = fullName, PullNumber = number,
            Title = string.IsNullOrWhiteSpace(title) ? $"{fullName} #{number}" : title,
            Summary = result.Summary, CommentCount = result.Comments.Count,
            CriticalCount = result.Comments.Count(c => c.Severity == "critical"),
            RanByUserId = "system", RanByName = "CodeSage (auto)"
        });

        // Post the findings back onto the GitHub PR.
        try
        {
            var body = $"## 🔍 CodeSage review\n\n{result.Summary}\n\n" +
                string.Join("\n", result.Comments.Select(c => $"- **{c.Severity}** {(c.File is null ? "" : $"`{c.File}` — ")}{c.Comment}"));
            await _github.PostIssueCommentAsync(user.GitHubAccessToken, fullName, number, body);
        }
        catch { /* don't fail the job if commenting back fails */ }

        await _notify.SendToOrgAsync(orgId, "review.ran", $"Auto-reviewed {fullName} #{number} — {result.Comments.Count} findings");
    }

    // Recurring: trim audit logs older than 90 days.
    public async Task CleanupAuditLogs()
    {
        var cutoff = DateTime.UtcNow.AddDays(-90);
        await _db.AuditLogs.DeleteManyAsync(a => a.CreatedAt < cutoff);
    }
}