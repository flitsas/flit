namespace Flit.Modules.Security.Domain.Auth;

public sealed class UserAuthSnapshot
{
    public Guid UserId { get; init; }

    public string Email { get; init; } = string.Empty;

    public string Status { get; init; } = string.Empty;

    public string PasswordHash { get; init; } = string.Empty;

    public bool MustChangePassword { get; init; }

    public Guid TenantId { get; init; }

    public Guid RoleId { get; init; }

    public string RoleCode { get; init; } = string.Empty;

    public IReadOnlyList<string> PermissionSlugs { get; init; } = [];

    public bool IsTemporarilySuspended { get; init; }
}
