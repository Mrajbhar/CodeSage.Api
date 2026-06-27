using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace CodeSage.Api.Models;

public class Review
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? Id { get; set; }

    public string OrgId { get; set; } = null!;
    public string RepoFullName { get; set; } = null!;
    public int PullNumber { get; set; }
    public string Title { get; set; } = null!;
    public string Summary { get; set; } = "";
    public int CommentCount { get; set; }
    public int CriticalCount { get; set; }
    public List<ReviewComment> Comments { get; set; } = new();
    public string RanByUserId { get; set; } = null!;
    public string RanByName { get; set; } = null!;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class ReviewComment
{
    public string? File { get; set; }
    public string Severity { get; set; } = "info";
    public string Comment { get; set; } = "";
}