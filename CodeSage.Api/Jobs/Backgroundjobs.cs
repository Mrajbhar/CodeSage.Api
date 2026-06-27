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

        // Respect the org's monthly quota even for automated runs (atomic consume).
        var (ok, _, _) = await _usage.TryConsumeAsync(orgId, "review");
        if (!ok) { await _notify.SendToOrgAsync(orgId, "quota", $"Auto-review skipped for {fullName} #{number}: monthly quota reached."); return; }

        var diff = await _github.GetPullDiffAsync(user.GitHubAccessToken, fullName, number);
        if (string.IsNullOrWhiteSpace(diff)) { await _usage.RefundAsync(orgId, "review"); return; }

        var result = await _ai.ReviewDiffAsync(diff, $"{fullName} #{number}");

        // Apply this repo's review settings (severity threshold, ignore paths, file types).
        var watch = await _db.WatchedRepos.Find(w => w.OrgId == orgId && w.RepoFullName == fullName).FirstOrDefaultAsync();
        var comments = ApplySettings(result.Comments, watch);

        await _db.Reviews.InsertOneAsync(new Models.Review
        {
            OrgId = orgId,
            RepoFullName = fullName,
            PullNumber = number,
            Title = string.IsNullOrWhiteSpace(title) ? $"{fullName} #{number}" : title,
            Summary = result.Summary,
            CommentCount = comments.Count,
            CriticalCount = comments.Count(c => c.Severity == "critical"),
            Comments = comments.Select(c => new Models.ReviewComment { File = c.File, Severity = c.Severity, Comment = c.Comment }).ToList(),
            RanByUserId = "system",
            RanByName = "CodeSage (auto)"
        });

        // Post the findings back onto the GitHub PR (unless disabled for this repo).
        if (watch?.PostToGitHub != false && comments.Count > 0)
        {
            try
            {
                var body = $"## 🔍 CodeSage review\n\n{result.Summary}\n\n" +
                    string.Join("\n", comments.Select(c => $"- **{c.Severity}** {(c.File is null ? "" : $"`{c.File}` — ")}{c.Comment}"));
                await _github.PostIssueCommentAsync(user.GitHubAccessToken, fullName, number, body);
            }
            catch { /* don't fail the job if commenting back fails */ }
        }

        await _notify.SendToOrgAsync(orgId, "review.ran", $"Auto-reviewed {fullName} #{number} — {comments.Count} findings");
    }

    // Recurring: trim audit logs older than 90 days.
    public async Task CleanupAuditLogs()
    {
        var cutoff = DateTime.UtcNow.AddDays(-90);
        await _db.AuditLogs.DeleteManyAsync(a => a.CreatedAt < cutoff);
    }

    private static readonly Dictionary<string, int> SevRank = new()
    { ["info"] = 0, ["suggestion"] = 1, ["warning"] = 2, ["critical"] = 3 };

    // Filters AI findings by the repo's settings: severity floor, ignored paths, allowed file types.
    private static List<Dtos.ReviewCommentDto> ApplySettings(List<Dtos.ReviewCommentDto> comments, Models.WatchedRepo? w)
    {
        if (w is null) return comments;
        var floor = SevRank.TryGetValue(w.MinSeverity, out var f) ? f : 0;

        return comments.Where(c =>
        {
            var rank = SevRank.TryGetValue(c.Severity, out var r) ? r : 0;
            if (rank < floor) return false;

            var file = c.File ?? "";
            if (w.IgnorePaths.Any(p => !string.IsNullOrWhiteSpace(p) && file.Contains(p, StringComparison.OrdinalIgnoreCase)))
                return false;
            if (w.FileTypes.Count > 0 && !string.IsNullOrEmpty(file) &&
                !w.FileTypes.Any(ext => file.EndsWith(ext, StringComparison.OrdinalIgnoreCase)))
                return false;

            return true;
        }).ToList();
    }
}