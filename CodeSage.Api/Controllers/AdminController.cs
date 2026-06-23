using CodeSage.Api.Data;
using CodeSage.Api.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;

namespace CodeSage.Api.Controllers;

[ApiController]
[Authorize(Roles = "Admin")]   // RBAC: Admins only (step #6)
[Route("api/admin")]
public class AdminController : ControllerBase
{
    private readonly MongoContext _db;
    public AdminController(MongoContext db) => _db = db;

    [HttpGet("users")]
    public async Task<IActionResult> Users()
    {
        var users = await _db.Users.Find(FilterDefinition<Models.User>.Empty)
            .SortByDescending(u => u.CreatedAt).ToListAsync();

        var dtos = users.Select(u => new AdminUserDto(
            u.Id!, u.Email, u.DisplayName, u.Role,
            u.GitHubAccessToken is not null, u.CreatedAt));

        return Ok(dtos);
    }

    [HttpPut("users/{id}/role")]
    public async Task<IActionResult> SetRole(string id, UpdateRoleRequest req)
    {
        var role = req.Role?.Trim();
        if (role != "Admin" && role != "User")
            return BadRequest(new { message = "Role must be 'Admin' or 'User'." });

        var user = await _db.Users.Find(u => u.Id == id).FirstOrDefaultAsync();
        if (user is null) return NotFound();

        // Guard: never demote the last remaining admin, or nobody could administer the app.
        if (user.Role == "Admin" && role == "User")
        {
            var admins = await _db.Users.CountDocumentsAsync(u => u.Role == "Admin");
            if (admins <= 1)
                return BadRequest(new { message = "Can't demote the last remaining admin." });
        }

        await _db.Users.UpdateOneAsync(u => u.Id == id,
            Builders<Models.User>.Update.Set(u => u.Role, role));
        user.Role = role;

        return Ok(new AdminUserDto(
            user.Id!, user.Email, user.DisplayName, user.Role,
            user.GitHubAccessToken is not null, user.CreatedAt));
    }
}