using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace CodeSage.Api.Models;

public class User
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? Id { get; set; }

    public string Email { get; set; } = null!;
    public string? PasswordHash { get; set; }     // null for OAuth-only users
    public string DisplayName { get; set; } = null!;
    public string Role { get; set; } = "User";    // "User" or "Admin"

    // GitHub link (used for OAuth sign-in and repo access)
    public long? GitHubId { get; set; }
    public string? GitHubLogin { get; set; }
    public string? GitHubAccessToken { get; set; }
    public string? AvatarUrl { get; set; }

    // Google link
    public string? GoogleId { get; set; }

    public bool EmailVerified { get; set; } = false;
    public string? EmailVerificationToken { get; set; }

    public string? PasswordResetToken { get; set; }
    public DateTime? PasswordResetExpires { get; set; }

    public List<RefreshToken> RefreshTokens { get; set; } = new();
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class RefreshToken
{
    public string Token { get; set; } = null!;
    public DateTime ExpiresAt { get; set; }
    public bool IsExpired => DateTime.UtcNow >= ExpiresAt;
}