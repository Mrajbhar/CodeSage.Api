using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace CodeSage.Api.Models;

public class Usage
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? Id { get; set; }
    public string OrgId { get; set; } = null!;
    public string Period { get; set; } = null!;   // "yyyy-MM"
    public int ExplainCalls { get; set; }
    public int ReviewCalls { get; set; }
}

public class BillingEvent
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? Id { get; set; }
    public string OrgId { get; set; } = null!;
    public string Type { get; set; } = null!;     // upgrade | downgrade | charge
    public string Plan { get; set; } = null!;
    public int AmountCents { get; set; }
    public string? Note { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}