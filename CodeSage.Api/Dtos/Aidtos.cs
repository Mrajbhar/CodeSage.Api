namespace CodeSage.Api.Dtos;

public record RepoFileDto(string Path, long Size);
public record FileContentDto(string Path, string Content, bool Truncated);

public record ExplainRequest(string Code, string? Path, string? Language);
public record ExplainResponse(string Explanation);