namespace CodeSage.Api.Dtos;

public record IndexRequest(string FullName);
public record SearchResultDto(string Path, string RepoFullName, double Score);