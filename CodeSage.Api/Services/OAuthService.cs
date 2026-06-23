using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Web;
using CodeSage.Api.Data;
using CodeSage.Api.Dtos;
using CodeSage.Api.Models;
using CodeSage.Api.Settings;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace CodeSage.Api.Services;

public class OAuthService
{
    private readonly IHttpClientFactory _http;
    private readonly MongoContext _db;
    private readonly AuthService _auth;
    private readonly OAuthSettings _oauth;
    private readonly AppSettings _app;

    public OAuthService(IHttpClientFactory http, MongoContext db, AuthService auth,
        IOptions<OAuthSettings> oauth, IOptions<AppSettings> app)
    {
        _http = http;
        _db = db;
        _auth = auth;
        _oauth = oauth.Value;
        _app = app.Value;
    }

    private string GitHubCallback => $"{_app.ApiBaseUrl}/api/auth/github/callback";
    private string GoogleCallback => $"{_app.ApiBaseUrl}/api/auth/google/callback";

    public string BuildGitHubAuthorizeUrl(string state)
    {
        var q = HttpUtility.ParseQueryString(string.Empty);
        q["client_id"] = _oauth.GitHub.ClientId;
        q["redirect_uri"] = GitHubCallback;
        q["scope"] = "read:user user:email repo";
        q["state"] = state;
        return "https://github.com/login/oauth/authorize?" + q;
    }

    public string BuildGoogleAuthorizeUrl(string state)
    {
        var q = HttpUtility.ParseQueryString(string.Empty);
        q["client_id"] = _oauth.Google.ClientId;
        q["redirect_uri"] = GoogleCallback;
        q["response_type"] = "code";
        q["scope"] = "openid email profile";
        q["state"] = state;
        return "https://accounts.google.com/o/oauth2/v2/auth?" + q;
    }

    // ---------- GitHub ----------
    public async Task<AuthResponse> HandleGitHubCallbackAsync(string code, string? linkUserId)
    {
        var client = _http.CreateClient();

        // 1) code -> access token
        var tokenJson = await SendJson(client, BuildReq(HttpMethod.Post,
            "https://github.com/login/oauth/access_token", new Dictionary<string, string>
            {
                ["client_id"] = _oauth.GitHub.ClientId,
                ["client_secret"] = _oauth.GitHub.ClientSecret,
                ["code"] = code,
                ["redirect_uri"] = GitHubCallback
            }));
        var ghToken = tokenJson.RootElement.GetProperty("access_token").GetString()!;

        // 2) token -> profile + primary email
        var profile = await GitHubGet<GitHubUser>(client, ghToken, "https://api.github.com/user");
        var emails = await GitHubGet<List<GitHubEmail>>(client, ghToken, "https://api.github.com/user/emails");
        var email = emails?.FirstOrDefault(e => e.Primary && e.Verified)?.Email
                    ?? emails?.FirstOrDefault()?.Email
                    ?? $"{profile.Login}@users.noreply.github.com";

        // 3) resolve the user
        User user;
        if (linkUserId is not null)
        {
            user = await _db.Users.Find(u => u.Id == linkUserId).FirstAsync();
        }
        else
        {
            user = await _db.Users.Find(u => u.GitHubId == profile.Id).FirstOrDefaultAsync()
                   ?? await _db.Users.Find(u => u.Email == email).FirstOrDefaultAsync()
                   ?? new User
                   {
                       Email = email,
                       DisplayName = string.IsNullOrWhiteSpace(profile.Name) ? profile.Login : profile.Name,
                       Role = await _auth.IsFirstUserAsync() ? "Admin" : "User"
                   };
        }

        user.GitHubId = profile.Id;
        user.GitHubLogin = profile.Login;
        user.GitHubAccessToken = ghToken;
        user.AvatarUrl ??= profile.AvatarUrl;

        if (user.Id is null) await _db.Users.InsertOneAsync(user);
        else await _db.Users.ReplaceOneAsync(u => u.Id == user.Id, user);

        return await _auth.IssueForUserAsync(user);
    }

    // ---------- Google ----------
    public async Task<AuthResponse> HandleGoogleCallbackAsync(string code)
    {
        var client = _http.CreateClient();

        var tokenJson = await SendJson(client, BuildReq(HttpMethod.Post,
            "https://oauth2.googleapis.com/token", new Dictionary<string, string>
            {
                ["client_id"] = _oauth.Google.ClientId,
                ["client_secret"] = _oauth.Google.ClientSecret,
                ["code"] = code,
                ["redirect_uri"] = GoogleCallback,
                ["grant_type"] = "authorization_code"
            }));
        var accessToken = tokenJson.RootElement.GetProperty("access_token").GetString()!;

        var info = await GoogleGet(client, accessToken);

        var user = await _db.Users.Find(u => u.GoogleId == info.Sub).FirstOrDefaultAsync()
                   ?? await _db.Users.Find(u => u.Email == info.Email).FirstOrDefaultAsync()
                   ?? new User
                   {
                       Email = info.Email,
                       DisplayName = info.Name ?? info.Email,
                       Role = await _auth.IsFirstUserAsync() ? "Admin" : "User"
                   };

        user.GoogleId = info.Sub;
        user.AvatarUrl ??= info.Picture;

        if (user.Id is null) await _db.Users.InsertOneAsync(user);
        else await _db.Users.ReplaceOneAsync(u => u.Id == user.Id, user);

        return await _auth.IssueForUserAsync(user);
    }

    // ---------- helpers ----------
    private static HttpRequestMessage BuildReq(HttpMethod method, string url, Dictionary<string, string> form)
    {
        var req = new HttpRequestMessage(method, url) { Content = new FormUrlEncodedContent(form) };
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return req;
    }

    private static async Task<JsonDocument> SendJson(HttpClient client, HttpRequestMessage req)
    {
        var resp = await client.SendAsync(req);
        resp.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
    }

    private static async Task<T> GitHubGet<T>(HttpClient client, string token, string url)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        req.Headers.UserAgent.ParseAdd("CodeSage");
        req.Headers.Accept.ParseAdd("application/vnd.github+json");
        var resp = await client.SendAsync(req);
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<T>())!;
    }

    private static async Task<GoogleUser> GoogleGet(HttpClient client, string token)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, "https://www.googleapis.com/oauth2/v3/userinfo");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var resp = await client.SendAsync(req);
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<GoogleUser>())!;
    }

    private class GitHubUser
    {
        [JsonPropertyName("id")] public long Id { get; set; }
        [JsonPropertyName("login")] public string Login { get; set; } = "";
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("avatar_url")] public string? AvatarUrl { get; set; }
    }
    private class GitHubEmail
    {
        [JsonPropertyName("email")] public string Email { get; set; } = "";
        [JsonPropertyName("primary")] public bool Primary { get; set; }
        [JsonPropertyName("verified")] public bool Verified { get; set; }
    }
    private class GoogleUser
    {
        [JsonPropertyName("sub")] public string Sub { get; set; } = "";
        [JsonPropertyName("email")] public string Email { get; set; } = "";
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("picture")] public string? Picture { get; set; }
    }
}
