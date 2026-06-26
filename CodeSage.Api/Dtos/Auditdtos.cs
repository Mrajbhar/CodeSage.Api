namespace CodeSage.Api.Dtos;

public record AuditDto(string Id, string ActorName, string Action, string Target, DateTime CreatedAt);