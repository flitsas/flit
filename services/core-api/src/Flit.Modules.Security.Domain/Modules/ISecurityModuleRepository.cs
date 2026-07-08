namespace Flit.Modules.Security.Domain.Modules;

public interface ISecurityModuleRepository
{
    Task<bool> CodeExistsAsync(string code, Guid? excludeId, CancellationToken ct);
    Task<Guid> CreateAsync(SecurityModuleData data, CancellationToken ct);
    Task<SecurityModuleDetail?> GetByIdAsync(Guid id, CancellationToken ct);
    Task UpdateAsync(Guid id, UpdateModuleData data, CancellationToken ct);
    Task DeactivateAsync(Guid id, CancellationToken ct);
    Task ActivateAsync(Guid id, CancellationToken ct);
    Task<bool> HasActivePermissionsAsync(Guid id, CancellationToken ct);
    Task SoftDeleteAsync(Guid id, CancellationToken ct);
    Task<IReadOnlyList<SecurityModuleSummary>> ListAsync(CancellationToken ct);
    /// <summary>
    /// Módulos y acciones accesibles según RBAC puro (HU #10664). Los módulos son transversales: no
    /// existe habilitación por empresa. Con <paramref name="includeAll"/>=<c>true</c> (constructor de
    /// roles SuperAdmin) devuelve todos los módulos activos con sus acciones; en otro caso (caller
    /// tenant) devuelve solo los módulos cuyas acciones (slugs) están en <paramref name="permissionSlugs"/>.
    /// </summary>
    Task<IReadOnlyList<AccessibleModuleDto>> ListAccessibleAsync(
        IReadOnlyList<string> permissionSlugs,
        bool includeAll,
        CancellationToken ct);
}

public sealed record SecurityModuleData(string Code, string Name, string? Description, short SortOrder);
public sealed record UpdateModuleData(string Name, string? Description, short SortOrder);
public sealed record SecurityModuleDetail(Guid Id, string Code, string Name, string? Description, short SortOrder, bool IsActive, DateTimeOffset? DeletedAt);
public sealed record SecurityModuleSummary(Guid Id, string Code, string Name, string? Description, short SortOrder, bool IsActive, int PermissionCount);
public sealed record AccessibleModuleDto(Guid Id, string Code, string Name, short SortOrder, IReadOnlyList<AccessibleActionDto> Actions);
public sealed record AccessibleActionDto(Guid Id, string Slug, string Name);
