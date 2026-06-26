using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace CodeSage.Api.Models;

public class AuditLog
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? Id { get; set; }

    public string OrgId { get; set; } = null!;
    public string? ActorId { get; set; }
    public string ActorName { get; set; } = "System";
    public string Action { get; set; } = null!;   // e.g. org.created, member.invited, plan.changed, review.ran
    public string Target { get; set; } = "";       // human-readable subject of the action
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}