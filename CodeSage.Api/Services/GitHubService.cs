using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using CodeSage.Api.Dtos;

namespace CodeSage.Api.Services;

public class GitHubService
{
    private const int Weeks = 12;
    private const int ActivityRepoLimit = 8;
    private readonly IHttpClientFactory _http;
    public GitHubService(IHttpClientFactory http) => _http = http;

    public async Task<List<RepoDto>> GetReposAsync(string accessToken)
    {
        var repos = await FetchReposAsync(accessToken);
        return repos.Select(r => new RepoDto(
            r.Name, r.FullName, r.Private, r.Description, r.Language,
            r.Stars, r.HtmlUrl, r.UpdatedAt)).ToList();
    }

    // Powers the combined "Codebase map" panel: repos + per-repo commit activity + language mix.
    public async Task<OverviewDto> GetOverviewAsync(string accessToken)
    {
        var repos = await FetchReposAsync(accessToken);

        // language composition by primary language (share of repositories)
        var withLang = repos.Where(r => r.Language is not null).ToList();
        var langGroups = withLang
            .GroupBy(r => r.Language!)
            .Select(g => new { Name = g.Key, Count = g.Count() })
            .OrderByDescending(g => g.Count)
            .ToList();
        var langTotal = withLang.Count == 0 ? 1 : withLang.Count;
        var languages = langGroups.Take(6)
            .Select(g => new LanguageSliceDto(g.Name, (int)Math.Round(g.Count * 100.0 / langTotal)))
            .ToList();

        // commit activity for the most-recently-updated repositories
        var client = _http.CreateClient();
        var since = DateTime.UtcNow.AddDays(-7 * Weeks).ToString("yyyy-MM-ddTHH:mm:ssZ");
        var top = repos.Take(ActivityRepoLimit).ToList();

        var activity = await Task.WhenAll(top.Select(async r =>
        {
            var (weeks, total) = await GetCommitWeeksAsync(client, accessToken, r.FullName, since);
            return new RepoActivityDto(r.Name, r.FullName, r.Language, r.Stars, r.HtmlUrl, r.UpdatedAt, weeks, total);
        }));

        var commitsThisWeek = activity.Sum(a => a.Weeks.Length > 0 ? a.Weeks[^1] : 0);
        var totals = new OverviewTotalsDto(repos.Count, langGroups.Count, commitsThisWeek);

        return new OverviewDto(activity.ToList(), languages, totals);
    }

    // ---- Phase 2: repository files ----
    public async Task<List<RepoFileDto>> GetTreeAsync(string token, string fullName)
    {
        var client = _http.CreateClient();

        var repoResp = await client.SendAsync(Authed(HttpMethod.Get, $"https://api.github.com/repos/{fullName}", token));
        repoResp.EnsureSuccessStatusCode();
        var repo = await repoResp.Content.ReadFromJsonAsync<GitHubRepo>();
        var branch = string.IsNullOrWhiteSpace(repo?.DefaultBranch) ? "main" : repo!.DefaultBranch;

        var treeResp = await client.SendAsync(Authed(HttpMethod.Get,
            $"https://api.github.com/repos/{fullName}/git/trees/{branch}?recursive=1", token));
        treeResp.EnsureSuccessStatusCode();
        var tree = await treeResp.Content.ReadFromJsonAsync<GitHubTree>() ?? new();

        return tree.Tree
            .Where(t => t.Type == "blob")
            .Take(500)
            .Select(t => new RepoFileDto(t.Path, t.Size ?? 0))
            .ToList();
    }

    public async Task<FileContentDto> GetFileContentAsync(string token, string fullName, string path)
    {
        var client = _http.CreateClient();
        var resp = await client.SendAsync(Authed(HttpMethod.Get,
            $"https://api.github.com/repos/{fullName}/contents/{Uri.EscapeDataString(path).Replace("%2F", "/")}", token));
        resp.EnsureSuccessStatusCode();

        var file = await resp.Content.ReadFromJsonAsync<GitHubContent>();
        if (file?.Content is null || file.Encoding != "base64")
            return new FileContentDto(path, "", true);

        if (file.Size > 200_000)
            return new FileContentDto(path, "", true);

        var bytes = Convert.FromBase64String(file.Content.Replace("\n", ""));
        var text = System.Text.Encoding.UTF8.GetString(bytes);
        return new FileContentDto(path, text, false);
    }

    // ---- Phase 2: pull requests ----
    public async Task<List<PullDto>> GetPullsAsync(string token, string fullName)
    {
        var client = _http.CreateClient();
        var resp = await client.SendAsync(Authed(HttpMethod.Get,
            $"https://api.github.com/repos/{fullName}/pulls?state=open&per_page=50", token));
        resp.EnsureSuccessStatusCode();
        var pulls = await resp.Content.ReadFromJsonAsync<List<GitHubPull>>() ?? new();
        return pulls
            .Select(p => new PullDto(p.Number, p.Title, p.User?.Login ?? "unknown", p.HtmlUrl, p.CreatedAt))
            .ToList();
    }

    // Posts CodeSage's findings back onto the GitHub PR as a comment.
    public async Task PostIssueCommentAsync(string token, string fullName, int number, string body)
    {
        var client = _http.CreateClient();
        var req = Authed(HttpMethod.Post, $"https://api.github.com/repos/{fullName}/issues/{number}/comments", token);
        req.Content = JsonContent.Create(new { body });
        var resp = await client.SendAsync(req);
        resp.EnsureSuccessStatusCode();
    }

    public async Task<string> GetPullDiffAsync(string token, string fullName, int number)
    {
        var client = _http.CreateClient();
        var req = new HttpRequestMessage(HttpMethod.Get,
            $"https://api.github.com/repos/{fullName}/pulls/{number}");
        req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        req.Headers.UserAgent.ParseAdd("CodeSage");
        req.Headers.Accept.ParseAdd("application/vnd.github.v3.diff");

        var resp = await client.SendAsync(req);
        resp.EnsureSuccessStatusCode();
        var diff = await resp.Content.ReadAsStringAsync();
        return diff.Length > 30_000 ? diff[..30_000] : diff;   // keep prompt within bounds
    }

    private async Task<List<GitHubRepo>> FetchReposAsync(string accessToken)
    {
        var client = _http.CreateClient();
        var req = Authed(HttpMethod.Get,
            "https://api.github.com/user/repos?per_page=100&sort=updated&affiliation=owner,collaborator",
            accessToken);
        var resp = await client.SendAsync(req);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<List<GitHubRepo>>() ?? new();
    }

    private async Task<(int[] weeks, int total)> GetCommitWeeksAsync(
        HttpClient client, string token, string fullName, string since)
    {
        var weeks = new int[Weeks];
        try
        {
            var req = Authed(HttpMethod.Get,
                $"https://api.github.com/repos/{fullName}/commits?per_page=100&since={since}", token);
            var resp = await client.SendAsync(req);
            if (!resp.IsSuccessStatusCode) return (weeks, 0);   // empty repo / no access

            var commits = await resp.Content.ReadFromJsonAsync<List<GitHubCommit>>() ?? new();
            var now = DateTime.UtcNow;
            var total = 0;
            foreach (var c in commits)
            {
                var d = c.Commit?.Author?.Date;
                if (d is null) continue;
                var weeksAgo = (int)((now - d.Value).TotalDays / 7);
                if (weeksAgo >= 0 && weeksAgo < Weeks) { weeks[Weeks - 1 - weeksAgo]++; total++; }
            }
            return (weeks, total);
        }
        catch
        {
            return (weeks, 0);
        }
    }

    private static HttpRequestMessage Authed(HttpMethod method, string url, string token)
    {
        var req = new HttpRequestMessage(method, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        req.Headers.UserAgent.ParseAdd("CodeSage");
        req.Headers.Accept.ParseAdd("application/vnd.github+json");
        return req;
    }

    private class GitHubRepo
    {
        [JsonPropertyName("name")] public string Name { get; set; } = "";
        [JsonPropertyName("full_name")] public string FullName { get; set; } = "";
        [JsonPropertyName("private")] public bool Private { get; set; }
        [JsonPropertyName("description")] public string? Description { get; set; }
        [JsonPropertyName("language")] public string? Language { get; set; }
        [JsonPropertyName("stargazers_count")] public int Stars { get; set; }
        [JsonPropertyName("html_url")] public string HtmlUrl { get; set; } = "";
        [JsonPropertyName("updated_at")] public DateTime? UpdatedAt { get; set; }
        [JsonPropertyName("default_branch")] public string? DefaultBranch { get; set; }
    }
    private class GitHubCommit { [JsonPropertyName("commit")] public CommitInfo? Commit { get; set; } }
    private class CommitInfo { [JsonPropertyName("author")] public CommitAuthor? Author { get; set; } }
    private class CommitAuthor { [JsonPropertyName("date")] public DateTime? Date { get; set; } }

    private class GitHubTree
    {
        [JsonPropertyName("tree")] public List<GitHubTreeEntry> Tree { get; set; } = new();
    }
    private class GitHubTreeEntry
    {
        [JsonPropertyName("path")] public string Path { get; set; } = "";
        [JsonPropertyName("type")] public string Type { get; set; } = "";
        [JsonPropertyName("size")] public long? Size { get; set; }
    }
    private class GitHubContent
    {
        [JsonPropertyName("content")] public string? Content { get; set; }
        [JsonPropertyName("encoding")] public string? Encoding { get; set; }
        [JsonPropertyName("size")] public long Size { get; set; }
    }
    private class GitHubPull
    {
        [JsonPropertyName("number")] public int Number { get; set; }
        [JsonPropertyName("title")] public string Title { get; set; } = "";
        [JsonPropertyName("html_url")] public string HtmlUrl { get; set; } = "";
        [JsonPropertyName("created_at")] public DateTime CreatedAt { get; set; }
        [JsonPropertyName("user")] public GitHubUserRef? User { get; set; }
    }
    private class GitHubUserRef { [JsonPropertyName("login")] public string Login { get; set; } = ""; }
}