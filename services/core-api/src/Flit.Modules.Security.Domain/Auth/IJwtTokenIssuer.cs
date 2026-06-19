namespace Flit.Modules.Security.Domain.Auth;

public sealed class IssuedAccessToken
{
    public string Token { get; init; } = string.Empty;

    public int ExpiresInSeconds { get; init; }
}

public interface IJwtTokenIssuer
{
    IssuedAccessToken IssueToken(
        Guid userId,
        string email,
        Guid tenantId,
        Guid roleId,
        string roleCode,
        IReadOnlyList<string> permissionSlugs);
}
