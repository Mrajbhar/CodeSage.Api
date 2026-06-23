using CodeSage.Api.Data;
using CodeSage.Api.Dtos;
using CodeSage.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;
using System.Security.Claims;
using System.Xml.Linq;

namespace CodeSage.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/comments")]
public class CommentsController : ControllerBase
{
    private readonly MongoContext _db;
    public CommentsController(MongoContext db) => _db = db;

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string target)
    {
        if (string.IsNullOrWhiteSpace(target)) return BadRequest(new { message = "Missing target." });
        var items = await _db.Comments.Find(c => c.Target == target)
            .SortBy(c => c.CreatedAt).ToListAsync();
        return Ok(items.Select(ToDto));
    }

    [HttpPost]
    public async Task<IActionResult> Create(CreateCommentRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Target) || string.IsNullOrWhiteSpace(req.Body))
            return BadRequest(new { message = "Target and body are required." });

        var comment = new Comment
        {
            Target = req.Target.Trim(),
            Body = req.Body.Trim(),
            UserId = CurrentUserId()!,
            UserName = User.FindFirstValue("displayName") ?? "Someone"
        };
        await _db.Comments.InsertOneAsync(comment);
        return Ok(ToDto(comment));
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id)
    {
        var comment = await _db.Comments.Find(c => c.Id == id).FirstOrDefaultAsync();
        if (comment is null) return NotFound();

        var isAdmin = User.IsInRole("Admin");
        if (comment.UserId != CurrentUserId() && !isAdmin)
            return Forbid();

        await _db.Comments.DeleteOneAsync(c => c.Id == id);
        return NoContent();
    }

    private string? CurrentUserId() =>
        User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");

    private static CommentDto ToDto(Comment c) =>
        new(c.Id!, c.UserId, c.UserName, c.Body, c.CreatedAt);
}