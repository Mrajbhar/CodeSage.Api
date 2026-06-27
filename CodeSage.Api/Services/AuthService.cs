using CodeSage.Api.Data;
using CodeSage.Api.Dtos;
using CodeSage.Api.Models;
using MongoDB.Driver;

namespace CodeSage.Api.Services;

public class AuthService
{
    private readonly MongoContext _db;
    private readonly TokenService _tokens;

    public AuthService(MongoContext db, TokenService tokens)
    {
        _db = db;
        _tokens = tokens;
    }

    // Returns the created user (caller sends the verification email), or null if the email is taken.
    public async Task<User?> RegisterAsync(RegisterRequest req)
    {
        var existing = await _db.Users.Find(u => u.Email == req.Email).FirstOrDefaultAsync();
        if (existing is not null) return null;

        var user = new User
        {
            Email = req.Email,
            DisplayName = req.DisplayName,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(req.Password),
            Role = await IsFirstUserAsync() ? "Admin" : "User",
            EmailVerified = false,
            EmailVerificationToken = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(24)).ToLowerInvariant()
        };

        await _db.Users.InsertOneAsync(user);
        return user;   // NOT logged in yet — must verify first
    }

    // (resp, error): error "verify" => valid creds but email not verified.
    public async Task<(AuthResponse? resp, string? error)> LoginAsync(LoginRequest req)
    {
        var user = await _db.Users.Find(u => u.Email == req.Email).FirstOrDefaultAsync();
        if (user?.PasswordHash is null) return (null, null);
        if (!BCrypt.Net.BCrypt.Verify(req.Password, user.PasswordHash)) return (null, null);
        if (!user.EmailVerified) return (null, "verify");
        return (await IssueForUserAsync(user), null);
    }

    public enum VerifyResult { Verified, AlreadyVerified, Invalid }

    public async Task<VerifyResult> VerifyEmailAsync(string token)
    {
        if (string.IsNullOrWhiteSpace(token)) return VerifyResult.Invalid;

        var user = await _db.Users.Find(u => u.EmailVerificationToken == token).FirstOrDefaultAsync();
        if (user is null) return VerifyResult.Invalid;
        if (user.EmailVerified) return VerifyResult.AlreadyVerified;

        user.EmailVerified = true;
        // Keep the token so a repeat click (StrictMode double-fire, refresh, re-open)
        // still resolves to this user and returns AlreadyVerified instead of "invalid".
        await _db.Users.ReplaceOneAsync(u => u.Id == user.Id, user);
        return VerifyResult.Verified;
    }

    // Regenerates a token for an unverified account; returns it so the caller can email it. Null if nothing to do.
    public async Task<User?> PrepareResendAsync(string email)
    {
        var user = await _db.Users.Find(u => u.Email == email.ToLowerInvariant()).FirstOrDefaultAsync();
        if (user is null || user.EmailVerified) return null;
        user.EmailVerificationToken = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();
        await _db.Users.ReplaceOneAsync(u => u.Id == user.Id, user);
        return user;
    }

    public async Task<AuthResponse?> RefreshAsync(string refreshToken)
    {
        var user = await _db.Users
            .Find(u => u.RefreshTokens.Any(t => t.Token == refreshToken)).FirstOrDefaultAsync();
        if (user is null) return null;

        var stored = user.RefreshTokens.First(t => t.Token == refreshToken);
        user.RefreshTokens.Remove(stored);
        if (stored.IsExpired)
        {
            await SaveTokensAsync(user);
            return null;
        }
        return await IssueForUserAsync(user);
    }

    public async Task LogoutAsync(string refreshToken)
    {
        var user = await _db.Users
            .Find(u => u.RefreshTokens.Any(t => t.Token == refreshToken)).FirstOrDefaultAsync();
        if (user is null) return;
        user.RefreshTokens.RemoveAll(t => t.Token == refreshToken);
        await SaveTokensAsync(user);
    }

    // Issues a fresh access + refresh pair for an already-resolved user (used by
    // password login, refresh rotation, AND the OAuth callbacks).
    public async Task<AuthResponse> IssueForUserAsync(User user)
    {
        var refresh = _tokens.CreateRefreshToken();
        user.RefreshTokens.Add(refresh);
        await SaveTokensAsync(user);
        var access = _tokens.CreateAccessToken(user);
        return new AuthResponse(access, refresh.Token, ToDto(user));
    }

    public Task<bool> IsFirstUserAsync() =>
        _db.Users.CountDocumentsAsync(FilterDefinition<User>.Empty)
            .ContinueWith(t => t.Result == 0);

    public static UserDto ToDto(User u) => new(
        u.Id!, u.Email, u.DisplayName, u.Role,
        GitHubConnected: u.GitHubAccessToken is not null,
        GitHubLogin: u.GitHubLogin, AvatarUrl: u.AvatarUrl);

    private Task SaveTokensAsync(User user)
    {
        var update = Builders<User>.Update.Set(u => u.RefreshTokens, user.RefreshTokens);
        return _db.Users.UpdateOneAsync(u => u.Id == user.Id, update);
    }
}