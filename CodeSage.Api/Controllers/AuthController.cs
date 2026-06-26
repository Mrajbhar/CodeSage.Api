using System.Security.Claims;
using CodeSage.Api.Data;
using CodeSage.Api.Dtos;
using CodeSage.Api.Services;
using CodeSage.Api.Settings;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace CodeSage.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly AuthService _auth;
    private readonly OAuthService _oauth;
    private readonly TokenService _tokens;
    private readonly MongoContext _db;
    private readonly AppSettings _app;
    private readonly Services.Email.IEmailSender _email;

    public AuthController(AuthService auth, OAuthService oauth, TokenService tokens,
        MongoContext db, IOptions<AppSettings> app, Services.Email.IEmailSender email)
    {
        _auth = auth; _oauth = oauth; _tokens = tokens; _db = db; _app = app.Value; _email = email;
    }

    // ---------- email / password ----------
    [EnableRateLimiting("auth")]
    [HttpPost("register")]
    public async Task<IActionResult> Register(RegisterRequest req)
    {
        var result = await _auth.RegisterAsync(req);
        return result is null ? Conflict(new { message = "Email is already registered." }) : Ok(result);
    }

    [EnableRateLimiting("auth")]
    [HttpPost("login")]
    public async Task<IActionResult> Login(LoginRequest req)
    {
        var result = await _auth.LoginAsync(req);
        return result is null ? Unauthorized(new { message = "Invalid email or password." }) : Ok(result);
    }

    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh(RefreshRequest req)
    {
        var result = await _auth.RefreshAsync(req.RefreshToken);
        return result is null ? Unauthorized(new { message = "Invalid or expired refresh token." }) : Ok(result);
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout(RefreshRequest req)
    {
        await _auth.LogoutAsync(req.RefreshToken);
        return NoContent();
    }

    // Always returns 200 so we don't reveal which emails are registered.
    [EnableRateLimiting("auth")]
    [HttpPost("forgot-password")]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordRequest req)
    {
        var email = req.Email?.Trim().ToLowerInvariant();
        var user = await _db.Users.Find(u => u.Email == email).FirstOrDefaultAsync();
        if (user is not null && user.PasswordHash is not null)
        {
            user.PasswordResetToken = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();
            user.PasswordResetExpires = DateTime.UtcNow.AddHours(1);
            await _db.Users.ReplaceOneAsync(u => u.Id == user.Id, user);

            var link = $"{_app.FrontendBaseUrl}/reset-password?token={user.PasswordResetToken}";
            await _email.SendAsync(user.Email, "Reset your CodeSage password",
                $"<p>Hi {user.DisplayName},</p><p>Reset your password with the link below (valid for 1 hour):</p><p><a href=\"{link}\">{link}</a></p><p>If you didn't request this, ignore this email.</p>");
        }
        return Ok(new { message = "If that email is registered, a reset link is on its way." });
    }

    [EnableRateLimiting("auth")]
    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword(ResetPasswordRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.NewPassword) || req.NewPassword.Length < 8)
            return BadRequest(new { message = "Password must be at least 8 characters." });

        var user = await _db.Users.Find(u => u.PasswordResetToken == req.Token).FirstOrDefaultAsync();
        if (user is null || user.PasswordResetExpires is null || user.PasswordResetExpires < DateTime.UtcNow)
            return BadRequest(new { message = "This reset link is invalid or has expired." });

        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(req.NewPassword);
        user.PasswordResetToken = null;
        user.PasswordResetExpires = null;
        user.RefreshTokens.Clear();   // sign out everywhere after a reset
        await _db.Users.ReplaceOneAsync(u => u.Id == user.Id, user);

        return Ok(new { message = "Password updated. You can sign in now." });
    }

    [Authorize]
    [HttpGet("me")]
    public async Task<IActionResult> Me()
    {
        var user = await _db.Users.Find(u => u.Id == CurrentUserId()).FirstOrDefaultAsync();
        return user is null ? NotFound() : Ok(AuthService.ToDto(user));
    }

    // ---------- GitHub OAuth ----------
    [HttpGet("github/login")]
    public IActionResult GitHubLogin([FromQuery] string? state)
    {
        // state is either a link-state (from link-intent) or freshly minted for CSRF.
        var s = state ?? _tokens.CreateStateToken(null);
        return Redirect(_oauth.BuildGitHubAuthorizeUrl(s));
    }

    [HttpGet("github/callback")]
    public async Task<IActionResult> GitHubCallback([FromQuery] string code, [FromQuery] string state)
    {
        var (ok, linkUser) = _tokens.ValidateStateToken(state);
        if (!ok) return RedirectToClient(error: "Invalid sign-in state. Please try again.");
        try
        {
            var result = await _oauth.HandleGitHubCallbackAsync(code, linkUser);
            return RedirectToClient(result);
        }
        catch
        {
            return RedirectToClient(error: "GitHub sign-in failed. Please try again.");
        }
    }

    // Logged-in users call this first to get a signed state that links GitHub to their account.
    [Authorize]
    [HttpPost("github/link-intent")]
    public IActionResult GitHubLinkIntent() =>
        Ok(new LinkStateResponse(_tokens.CreateStateToken(CurrentUserId())));

    // ---------- Google OAuth ----------
    [HttpGet("google/login")]
    public IActionResult GoogleLogin() =>
        Redirect(_oauth.BuildGoogleAuthorizeUrl(_tokens.CreateStateToken(null)));

    [HttpGet("google/callback")]
    public async Task<IActionResult> GoogleCallback([FromQuery] string code, [FromQuery] string state)
    {
        var (ok, _) = _tokens.ValidateStateToken(state);
        if (!ok) return RedirectToClient(error: "Invalid sign-in state. Please try again.");
        try
        {
            var result = await _oauth.HandleGoogleCallbackAsync(code);
            return RedirectToClient(result);
        }
        catch
        {
            return RedirectToClient(error: "Google sign-in failed. Please try again.");
        }
    }

    // ---------- helpers ----------
    private string? CurrentUserId() =>
        User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");

    private IActionResult RedirectToClient(AuthResponse? result = null, string? error = null)
    {
        var baseUrl = $"{_app.FrontendBaseUrl}/auth/callback#";
        if (error is not null)
            return Redirect(baseUrl + "error=" + Uri.EscapeDataString(error));

        var access = Uri.EscapeDataString(result!.AccessToken);
        var refresh = Uri.EscapeDataString(result.RefreshToken);
        return Redirect($"{baseUrl}access={access}&refresh={refresh}");
    }
}