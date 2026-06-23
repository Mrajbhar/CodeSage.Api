namespace CodeSage.Api.Dtos;

public record OrgDto(string Id, string Name, string Slug, string Plan, string MyRole, int MemberCount, DateTime CreatedAt);
public record MemberDto(string UserId, string Email, string DisplayName, string Role, DateTime JoinedAt);
public record InviteDto(string Email, string Role, string Token, DateTime CreatedAt);
public record OrgDetailDto(string Id, string Name, string Slug, string Plan, string MyRole,
    List<MemberDto> Members, List<InviteDto> Invitations);

public record CreateOrgRequest(string Name);
public record InviteRequest(string Email, string Role);
public record AcceptInviteRequest(string Token);
public record ChangeMemberRoleRequest(string Role);