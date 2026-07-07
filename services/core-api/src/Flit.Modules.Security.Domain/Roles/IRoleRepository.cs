namespace Flit.Modules.Security.Domain.Roles;

public interface IRoleRepository
{
    Task<bool> CodeExistsAsync(string targetEntityType, string code, CancellationToken ct);
    Task<Guid> CreateAsync(CreateRoleData data, CancellationToken ct);
    Task<RoleDetail?> GetByIdAsync(Guid id, CancellationToken ct);
    Task<bool> HasActiveUsersAsync(Guid id, CancellationToken ct);
    Task SoftDeleteAsync(Guid id, CancellationToken ct);
    Task<IReadOnlyList<RoleSummary>> ListByTargetEntityTypeAsync(string targetEntityType, CancellationToken ct);
    Task SetPermissionsAsync(Guid roleId, IReadOnlyList<Guid> permissionIds, CancellationToken ct);

    /// <summary>Activa/desactiva un rol del catálogo global (HU #10505). Gobernanza SuperAdmin (HU #10508).</summary>
    Task SetActiveAsync(Guid roleId, bool isActive, CancellationToken ct);
}

public sealed record CreateRoleData(string TargetEntityType, string Code, string Name, string? Description);

public sealed record RoleDetail(
    Guid Id,
    string TargetEntityType,
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
