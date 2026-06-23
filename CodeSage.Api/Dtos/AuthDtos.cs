namespace CodeSage.Api.Dtos;

public record RegisterRequest(string Email, string Password, string DisplayName);
public record LoginRequest(string Email, string Password);
public record RefreshRequest(string RefreshToken);

public record UserDto(
    string Id, string Email, string DisplayName, string Role,
    bool GitHubConnected, string? GitHubLogin, string? AvatarUrl);

public record AuthResponse(string AccessToken, string RefreshToken, UserDto User);

public record UpdateProfileRequest(string DisplayName);
public record UpdateRoleRequest(string Role);
public record LinkStateResponse(string State);

public record RepoDto(
    string Name, string FullName, bool Private, string? Description,
    string? Language, int Stars, string Url, DateTime? UpdatedAt);

public record AdminUserDto(
    string Id, string Email, string DisplayName, string Role,
    bool GitHubConnected, DateTime CreatedAt);