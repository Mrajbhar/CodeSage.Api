namespace CodeSage.Api.Dtos;

public record PullDto(int Number, string Title, string Author, string Url, DateTime CreatedAt);

public record ReviewRequest(string FullName, int Number);
public record ReviewCommentDto(string? File, string Severity, string Comment);
public record ReviewResultDto(string Summary, List<ReviewCommentDto> Comments);

public record CommentDto(string Id, string UserId, string UserName, string Body, DateTime CreatedAt);
public record CreateCommentRequest(string Target, string Body);