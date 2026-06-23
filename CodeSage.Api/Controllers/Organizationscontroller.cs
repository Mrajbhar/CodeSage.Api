using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using CodeSage.Api.Data;
using CodeSage.Api.Dtos;
using CodeSage.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;

namespace CodeSage.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/orgs")]
public class OrganizationsController : ControllerBase
{
    private readonly MongoContext _db;
    public OrganizationsController(MongoContext db) => _db = db;

    // ---- organizations ----
    [HttpGet]
    public async Task<IActionResult> Mine()
    {
        var uid = Uid();
        var orgs = await _db.Organizations.Find(o => o.Members.Any(m => m.UserId == uid)).ToListAsync();
        return Ok(orgs.Select(o => ToDto(o, uid)));
    }

    [HttpPost]
    public async Task<IActionResult> Create(CreateOrgRequest req)
    {
        var name = req.Name?.Trim();
        if (string.IsNullOrWhiteSpace(name))
            return BadRequest(new { message = "Organization name is required." });

        var org = new Organization
        {
            Name = name,
            Slug = Slugify(name),
            Members = { new Membership { UserId = Uid()!, Email = Email(), DisplayName = DisplayName(), Role = "Owner" } }
        };
        await _db.Organizations.InsertOneAsync(org);
        return Ok(ToDto(org, Uid()));
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> Detail(string id)
    {
        var org = await Load(id);
        if (org is null || !IsMember(org)) return NotFound();
        return Ok(ToDetail(org, Uid()));
    }

    // ---- invitations ----
    [HttpPost("{id}/invites")]
    public async Task<IActionResult> Invite(string id, InviteRequest req)
    {
        var org = await Load(id);
        if (org is null || !IsMember(org)) return NotFound();
        if (!CanManage(org)) return Forbid();

        var email = req.Email?.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(email)) return BadRequest(new { message = "Email is required." });
        var role = Normalize(req.Role);
        if (role == "Owner") return BadRequest(new { message = "Invite as Admin or Member." });

        if (org.Members.Any(m => m.Email.ToLowerInvariant() == email))
            return BadRequest(new { message = "That person is already a member." });

        var invite = new Invitation
        {
            Email = email,
            Role = role,
            InvitedBy = DisplayName(),
            Token = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant()
        };
        org.Invitations.RemoveAll(i => i.Email.ToLowerInvariant() == email);
        org.Invitations.Add(invite);
        await Save(org);

        return Ok(new InviteDto(invite.Email, invite.Role, invite.Token, invite.CreatedAt));
    }

    [HttpDelete("{id}/invites/{token}")]
    public async Task<IActionResult> CancelInvite(string id, string token)
    {
        var org = await Load(id);
        if (org is null || !IsMember(org)) return NotFound();
        if (!CanManage(org)) return Forbid();

        org.Invitations.RemoveAll(i => i.Token == token);
        await Save(org);
        return NoContent();
    }

    [HttpPost("invites/accept")]
    public async Task<IActionResult> Accept(AcceptInviteRequest req)
    {
        var org = await _db.Organizations.Find(o => o.Invitations.Any(i => i.Token == req.Token)).FirstOrDefaultAsync();
        if (org is null) return NotFound(new { message = "This invitation is no longer valid." });

        var invite = org.Invitations.First(i => i.Token == req.Token);
        if (!org.Members.Any(m => m.UserId == Uid()))
            org.Members.Add(new Membership { UserId = Uid()!, Email = Email(), DisplayName = DisplayName(), Role = invite.Role });
        org.Invitations.RemoveAll(i => i.Token == req.Token);
        await Save(org);

        return Ok(ToDto(org, Uid()));
    }

    // ---- members ----
    [HttpPut("{id}/members/{userId}/role")]
    public async Task<IActionResult> ChangeRole(string id, string userId, ChangeMemberRoleRequest req)
    {
        var org = await Load(id);
        if (org is null || !IsMember(org)) return NotFound();
        if (!CanManage(org)) return Forbid();

        var role = Normalize(req.Role);
        var member = org.Members.FirstOrDefault(m => m.UserId == userId);
        if (member is null) return NotFound();

        if (member.Role == "Owner" && role != "Owner" && org.Members.Count(m => m.Role == "Owner") <= 1)
            return BadRequest(new { message = "An organization must keep at least one owner." });

        member.Role = role;
        await Save(org);
        return Ok(ToDetail(org, Uid()));
    }

    [HttpDelete("{id}/members/{userId}")]
    public async Task<IActionResult> Remove(string id, string userId)
    {
        var org = await Load(id);
        if (org is null || !IsMember(org)) return NotFound();

        var isSelf = userId == Uid();
        if (!isSelf && !CanManage(org)) return Forbid();

        var member = org.Members.FirstOrDefault(m => m.UserId == userId);
        if (member is null) return NotFound();
        if (member.Role == "Owner" && org.Members.Count(m => m.Role == "Owner") <= 1)
            return BadRequest(new { message = "An organization must keep at least one owner." });

        org.Members.RemoveAll(m => m.UserId == userId);
        await Save(org);
        return NoContent();
    }

    // ---- helpers ----
    private Task<Organization> Load(string id) => _db.Organizations.Find(o => o.Id == id).FirstOrDefaultAsync()!;
    private Task Save(Organization o) => _db.Organizations.ReplaceOneAsync(x => x.Id == o.Id, o);

    private string? Uid() => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
    private string Email() => User.FindFirstValue(ClaimTypes.Email) ?? User.FindFirstValue("email") ?? "";
    private string DisplayName() => User.FindFirstValue("displayName") ?? "Someone";

    private bool IsMember(Organization o) => o.Members.Any(m => m.UserId == Uid());
    private bool CanManage(Organization o)
    {
        var me = o.Members.FirstOrDefault(m => m.UserId == Uid());
        return me?.Role is "Owner" or "Admin";
    }

    private static string Normalize(string? role) =>
        role?.Trim().ToLowerInvariant() switch { "owner" => "Owner", "admin" => "Admin", _ => "Member" };

    private static string Slugify(string name)
    {
        var s = Regex.Replace(name.ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');
        var suffix = Convert.ToHexString(RandomNumberGenerator.GetBytes(2)).ToLowerInvariant();
        return (string.IsNullOrEmpty(s) ? "org" : s) + "-" + suffix;
    }

    private static OrgDto ToDto(Organization o, string? uid)
    {
        var role = o.Members.FirstOrDefault(m => m.UserId == uid)?.Role ?? "Member";
        return new OrgDto(o.Id!, o.Name, o.Slug, o.Plan, role, o.Members.Count, o.CreatedAt);
    }

    private static OrgDetailDto ToDetail(Organization o, string? uid)
    {
        var role = o.Members.FirstOrDefault(m => m.UserId == uid)?.Role ?? "Member";
        return new OrgDetailDto(o.Id!, o.Name, o.Slug, o.Plan, role,
            o.Members.Select(m => new MemberDto(m.UserId, m.Email, m.DisplayName, m.Role, m.JoinedAt)).ToList(),
            o.Invitations.Select(i => new InviteDto(i.Email, i.Role, i.Token, i.CreatedAt)).ToList());
    }
}