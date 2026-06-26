using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace CodeSage.Api.Models;

public class Embedding
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? Id { get; set; }

    public string OrgId { get; set; } = null!;
    public string RepoFullName { get; set; } = null!;
    public string Path { get; set; } = null!;
    public float[] Vector { get; set; } = Array.Empty<float>();
}