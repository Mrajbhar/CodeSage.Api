using System.Security.Claims;
using CodeSage.Api.Data;
using CodeSage.Api.Dtos;
using CodeSage.Api.Services;
using CodeSage.Api.Settings;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
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
    private readonly ILogger<AuthController> _log;

    public AuthController(AuthService auth, OAuthService oauth, TokenService tokens,
        MongoContext db, IOptions<AppSettings> app, Services.Email.IEmailSender email, ILogger<AuthController> log)
    {
        _auth = auth; _oauth = oauth; _tokens = tokens; _db = db; _app = app.Value; _email = email; _log = log;
    }

    // ---------- email / password ----------
    [EnableRateLimiting("auth")]
    [HttpPost("register")]
    public async Task<IActionResult> Register(RegisterRequest req)
    {
        var user = await _auth.RegisterAsync(req);
        if (user is null) return Conflict(new { message = "Email is already registered." });

        await SendVerificationEmailAsync(user);
        return Ok(new { needsVerification = true, message = "Check your email to verify your account, then sign in." });
    }

    [EnableRateLimiting("auth")]
    [HttpPost("login")]
    public async Task<IActionResult> Login(LoginRequest req)
    {
        var (resp, error) = await _auth.LoginAsync(req);
        if (resp is not null) return Ok(resp);
        if (error == "verify")
            return StatusCode(StatusCodes.Status403Forbidden,
                new { needsVerification = true, message = "Please verify your email before signing in." });
        return Unauthorized(new { message = "Invalid email or password." });
    }

    [HttpPost("verify-email")]
    public async Task<IActionResult> VerifyEmail(VerifyEmailRequest req)
    {
        var result = await _auth.VerifyEmailAsync(req.Token);
        return result == AuthService.VerifyResult.Invalid
            ? BadRequest(new { message = "This verification link is invalid." })
            : Ok(new { message = "Email verified. You can sign in now." });   // Verified OR AlreadyVerified
    }

    [EnableRateLimiting("auth")]
    [HttpPost("resend-verification")]
    public async Task<IActionResult> ResendVerification(ForgotPasswordRequest req)
    {
        var user = await _auth.PrepareResendAsync(req.Email ?? "");
        if (user is not null) await SendVerificationEmailAsync(user);
        return Ok(new { message = "If that account needs verification, a new link is on its way." });
    }

    private async Task SendVerificationEmailAsync(Models.User user)
    {
        var link = $"{_app.FrontendBaseUrl}/verify-email?token={user.EmailVerificationToken}";
        try
        {
            await _email.SendAsync(user.Email, "Verify your CodeSage email",
                EmailTemplate(
                    heading: "Verify your email",
                    greeting: $"Hi {user.DisplayName},",
                    body: "Thanks for signing up for CodeSage. Confirm your email address to activate your account.",
                    buttonText: "Verify email",
                    buttonUrl: link,
                    footer: "If you didn't create a CodeSage account, you can safely ignore this email."));
        }
        catch (Exception ex)
        {
            // Never let an email outage break signup. The user can use "resend" once mail is configured.
            _log.LogWarning(ex, "Verification email to {Email} failed to send", user.Email);
        }
    }

    // Simple, clean, inline-styled HTML email (inline styles survive email clients).
    private static string EmailTemplate(string heading, string greeting, string body, string buttonText, string buttonUrl, string footer)
        => $@"<div style=""font-family:-apple-system,Segoe UI,Roboto,Helvetica,Arial,sans-serif;background:#0a0e14;padding:32px;color:#e6edf3"">
  <div style=""max-width:480px;margin:0 auto;background:#111722;border:1px solid #1f2733;border-radius:14px;padding:32px"">
    <div style=""font-size:18px;font-weight:600;color:#2dd4bf;margin-bottom:24px"">◇ CodeSage</div>
    <h1 style=""font-size:20px;font-weight:600;margin:0 0 16px;color:#fff"">{heading}</h1>
    <p style=""margin:0 0 8px;color:#c2ccd6"">{greeting}</p>
    <p style=""margin:0 0 24px;color:#9aa6b2;line-height:1.6"">{body}</p>
    <a href=""{buttonUrl}"" style=""display:inline-block;background:#2dd4bf;color:#06231f;font-weight:600;text-decoration:none;padding:11px 22px;border-radius:8px"">{buttonText}</a>
    <p style=""margin:24px 0 0;font-size:13px;color:#5b6673;line-height:1.6"">{footer}</p>
    <p style=""margin:8px 0 0;font-size:12px;color:#3d4550;word-break:break-all"">Or paste this link: {buttonUrl}</p>
  </div>
</div>";

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
                EmailTemplate(
                    heading: "Reset your password",
                    greeting: $"Hi {user.DisplayName},",
                    body: "We received a request to reset your CodeSage password. This link is valid for 1 hour.",
                    buttonText: "Reset password",
                    buttonUrl: link,
                    footer: "If you didn't request this, you can safely ignore this email — your password won't change."));
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
        catch (Exception ex)
        {
            _log.LogError(ex, "GitHub sign-in failed during callback");
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
        catch (Exception ex)
        {
            _log.LogError(ex, "Google sign-in failed during callback");
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