namespace CodeSage.Api.Dtos;

public record PullDto(int Number, string Title, string Author, string Url, DateTime CreatedAt);

public record ReviewRequest(string FullName, int Number, string? Title = null);
public record ReviewCommentDto(string? File, string Severity, string Comment);
public record ReviewResultDto(string Summary, List<ReviewCommentDto> Comments);

public record CommentDto(string Id, string UserId, string UserName, string Body, DateTime CreatedAt);
public record CreateCommentRequest(string Target, string Body);

public record ReviewSummaryDto(
    string Id, string RepoFullName, int PullNumber, string Title, string Summary,
    int CommentCount, int CriticalCount, string RanByName, DateTime CreatedAt);

public record WatchRequest(string FullName);

public record ReviewDetailDto(string Id, string RepoFullName, int PullNumber, string Title, string Summary,
    int CommentCount, int CriticalCount, string RanByName, DateTime CreatedAt, List<ReviewCommentDto> Comments);

public record RepoSettingsDto(string RepoFullName, string MinSeverity, List<string> IgnorePaths, List<string> FileTypes, bool PostToGitHub);