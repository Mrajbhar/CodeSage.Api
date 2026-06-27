using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace CodeSage.Api.Models;

// A repo enrolled for automated PR review, tied to the org + the user whose GitHub token we use.
public class WatchedRepo
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? Id { get; set; }
    public string OrgId { get; set; } = null!;
    public string RepoFullName { get; set; } = null!;
    public string UserId { get; set; } = null!;

    // Per-repo review configuration.
    public string MinSeverity { get; set; } = "info";       // info | suggestion | warning | critical
    public List<string> IgnorePaths { get; set; } = new();  // substrings/globs to skip (e.g. "test/", ".md")
    public List<string> FileTypes { get; set; } = new();    // extensions to include (empty = all), e.g. [".cs",".ts"]
    public bool PostToGitHub { get; set; } = true;          // comment findings back on the PR

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}