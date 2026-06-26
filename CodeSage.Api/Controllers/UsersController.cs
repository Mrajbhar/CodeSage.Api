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
[Route("api/users")]
public class UsersController : ControllerBase
{
    private readonly MongoContext _db;
    public UsersController(MongoContext db) => _db = db;

    [HttpPut("me")]
    public async Task<IActionResult> UpdateProfile(UpdateProfileRequest req)
    {
        var name = req.DisplayName?.Trim();
        if (string.IsNullOrWhiteSpace(name))
            return BadRequest(new { message = "Display name can't be empty." });

        var update = Builders<Models.User>.Update.Set(u => u.DisplayName, name);
        await _db.Users.UpdateOneAsync(u => u.Id == CurrentUserId(), update);

        var user = await _db.Users.Find(u => u.Id == CurrentUserId()).FirstOrDefaultAsync();
        return user is null ? NotFound() : Ok(AuthService.ToDto(user));
    }

    [HttpDelete("me/github")]
    public async Task<IActionResult> DisconnectGitHub()
    {
        var update = Builders<Models.User>.Update
            .Unset(u => u.GitHubAccessToken)
            .Unset(u => u.GitHubId)
            .Unset(u => u.GitHubLogin);
        await _db.Users.UpdateOneAsync(u => u.Id == CurrentUserId(), update);

        var user = await _db.Users.Find(u => u.Id == CurrentUserId()).FirstOrDefaultAsync();
        return user is null ? NotFound() : Ok(AuthService.ToDto(user));
    }

    [HttpDelete("me/google")]
    public async Task<IActionResult> DisconnectGoogle()
    {
        var user = await _db.Users.Find(u => u.Id == CurrentUserId()).FirstOrDefaultAsync();
        if (user is null) return NotFound();
        // Don't strand the account: must keep a password or another sign-in method.
        if (user.PasswordHash is null && user.GitHubId is null)
            return BadRequest(new { message = "Set a password first, or you'd lock yourself out." });

        await _db.Users.UpdateOneAsync(u => u.Id == CurrentUserId(),
            Builders<Models.User>.Update.Unset(u => u.GoogleId));
        var fresh = await _db.Users.Find(u => u.Id == CurrentUserId()).FirstOrDefaultAsync();
        return Ok(AuthService.ToDto(fresh!));
    }

    [HttpPost("me/change-password")]
    public async Task<IActionResult> ChangePassword(ChangePasswordRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.NewPassword) || req.NewPassword.Length < 8)
            return BadRequest(new { message = "New password must be at least 8 characters." });

        var user = await _db.Users.Find(u => u.Id == CurrentUserId()).FirstOrDefaultAsync();
        if (user is null) return NotFound();

        // If they already have a password, verify the current one. (OAuth-only users can set one.)
        if (user.PasswordHash is not null &&
            !BCrypt.Net.BCrypt.Verify(req.CurrentPassword ?? "", user.PasswordHash))
            return BadRequest(new { message = "Your current password is incorrect." });

        var update = Builders<Models.User>.Update
            .Set(u => u.PasswordHash, BCrypt.Net.BCrypt.HashPassword(req.NewPassword))
            .Set(u => u.RefreshTokens, new List<Models.RefreshToken>());   // sign out other sessions
        await _db.Users.UpdateOneAsync(u => u.Id == CurrentUserId(), update);
        return Ok(new { message = "Password updated." });
    }

    [HttpPost("me/change-email")]
    public async Task<IActionResult> ChangeEmail(ChangeEmailRequest req)
    {
        var email = req.NewEmail?.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(email) || !email.Contains('@'))
            return BadRequest(new { message = "Enter a valid email." });

        var taken = await _db.Users.Find(u => u.Email == email && u.Id != CurrentUserId()).AnyAsync();
        if (taken) return Conflict(new { message = "That email is already in use." });

        await _db.Users.UpdateOneAsync(u => u.Id == CurrentUserId(),
            Builders<Models.User>.Update.Set(u => u.Email, email));
        var user = await _db.Users.Find(u => u.Id == CurrentUserId()).FirstOrDefaultAsync();
        return Ok(AuthService.ToDto(user!));
    }

    [HttpDelete("me")]
    public async Task<IActionResult> DeleteAccount()
    {
        var uid = CurrentUserId();
        if (uid is null) return NotFound();

        // Remove the user from every org; delete orgs they solely own.
        var orgs = await _db.Organizations.Find(o => o.Members.Any(m => m.UserId == uid)).ToListAsync();
        foreach (var o in orgs)
        {
            var soleOwner = o.Members.Count == 1 && o.Members[0].UserId == uid;
            if (soleOwner) { await _db.Organizations.DeleteOneAsync(x => x.Id == o.Id); continue; }
            o.Members.RemoveAll(m => m.UserId == uid);
            await _db.Organizations.ReplaceOneAsync(x => x.Id == o.Id, o);
        }

        await _db.WatchedRepos.DeleteManyAsync(w => w.UserId == uid);
        await _db.Users.DeleteOneAsync(u => u.Id == uid);
        return Ok(new { message = "Account deleted." });
    }

    private string? CurrentUserId() =>
        User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
}