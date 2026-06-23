using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace CodeSage.Api.Models;

public class Organization
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? Id { get; set; }

    public string Name { get; set; } = null!;
    public string Slug { get; set; } = null!;
    public string Plan { get; set; } = "free";   // free | pro | team (Phase 3 #3)
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Razorpay subscription tracking
    public string? RazorpaySubscriptionId { get; set; }
    public string? RazorpaySubscriptionStatus { get; set; }   // created | authenticated | active | halted | cancelled

    public List<Membership> Members { get; set; } = new();
    public List<Invitation> Invitations { get; set; } = new();
}

public class Membership
{
    public string UserId { get; set; } = null!;
    public string Email { get; set; } = null!;
    public string DisplayName { get; set; } = null!;
    public string Role { get; set; } = "Member";   // Owner | Admin | Member
    public DateTime JoinedAt { get; set; } = DateTime.UtcNow;
}

public class Invitation
{
    public string Email { get; set; } = null!;
    public string Role { get; set; } = "Member";
    public string Token { get; set; } = null!;
    public string InvitedBy { get; set; } = null!;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}