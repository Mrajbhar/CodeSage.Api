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

    public async Task<AuthResponse?> RegisterAsync(RegisterRequest req)
    {
        var existing = await _db.Users.Find(u => u.Email == req.Email).FirstOrDefaultAsync();
        if (existing is not null) return null;

        var user = new User
        {
            Email = req.Email,
            DisplayName = req.DisplayName,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(req.Password),
            Role = await IsFirstUserAsync() ? "Admin" : "User"
        };

        await _db.Users.InsertOneAsync(user);
        return await IssueForUserAsync(user);
    }

    public async Task<AuthResponse?> LoginAsync(LoginRequest req)
    {
        var user = await _db.Users.Find(u => u.Email == req.Email).FirstOrDefaultAsync();
        if (user?.PasswordHash is null) return null;
        if (!BCrypt.Net.BCrypt.Verify(req.Password, user.PasswordHash)) return null;
        return await IssueForUserAsync(user);
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
