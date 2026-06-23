using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace CodeSage.Api.Models;

public class Comment
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? Id { get; set; }

    public string Target { get; set; } = null!; 
    public string UserId { get; set; } = null!;
    public string UserName { get; set; } = null!;
    public string Body { get; set; } = null!;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}