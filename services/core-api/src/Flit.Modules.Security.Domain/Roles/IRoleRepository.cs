namespace Flit.Modules.Security.Domain.Roles;

public interface IRoleRepository
{
    Task<bool> CodeExistsInTenantAsync(Guid tenantId, string code, CancellationToken ct);
    Task<Guid> CreateAsync(CreateRoleData data, CancellationToken ct);
    Task<RoleDetail?> GetByIdAsync(Guid id, CancellationToken ct);
    Task<bool> HasActiveUsersAsync(Guid id, CancellationToken ct);
    Task SoftDeleteAsync(Guid id, CancellationToken ct);
    Task<IReadOnlyList<RoleSummary>> ListByTenantAsync(Guid tenantId, CancellationToken ct);
    Task SetPermissionsAsync(Guid roleId, Guid tenantId, IReadOnlyList<Guid> permissionIds, CancellationToken ct);
}

public sealed record CreateRoleData(Guid TenantId, string Code, string Name, string? Description);

public sealed record RoleDetail(
    Guid Id,
    Guid TenantId,
    string Code,
    string Name,
    string? Description,
    bool IsSystem,
    bool IsActive,
    IReadOnlyList<PermissionSlug> Permissions);

public sealed record PermissionSlug(Guid Id, string Slug, string Name);

public sealed record RoleSummary(
    Guid Id,
    string Code,
    string Name,
    string? Description,
    bool IsSystem,
    int PermissionCount,
    DateTimeOffset CreatedAt);
