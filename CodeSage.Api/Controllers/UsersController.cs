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

    private string? CurrentUserId() =>
        User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
}
