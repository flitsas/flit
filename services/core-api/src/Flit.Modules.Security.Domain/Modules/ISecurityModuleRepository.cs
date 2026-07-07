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
    /// <paramref name="targetEntityType"/> (HU #10504 — scoping de módulos por tipo de tenant en el
    /// constructor de roles SuperAdmin): solo tiene efecto cuando <paramref name="includeAll"/> es
    /// <c>true</c> y viene con valor <c>"COMPANY"</c> o <c>"TRANSIT_OFFICE"</c>. En ese caso filtra los
    /// módulos "sin scope" (sin ningún <c>TenantModuleGrant</c>, visibles siempre) más los que tengan al
    /// menos un grant hacia un tenant del tipo pedido. Si es <c>null</c>, el comportamiento de
    /// <paramref name="includeAll"/>=true no cambia (todos los módulos, sin filtrar) — no tocar el branch
    /// <paramref name="includeAll"/>=false (HU10163, opt-in por tenant normal).
    /// </summary>
    Task<IReadOnlyList<AccessibleModuleDto>> ListAccessibleAsync(
        IReadOnlyList<string> permissionSlugs,
        bool includeAll,
        Guid? tenantId,
        string? targetEntityType,
        CancellationToken ct);
}

public sealed record SecurityModuleData(string Code, string Name, string? Description, short SortOrder);
public sealed record UpdateModuleData(string Name, string? Description, short SortOrder);
public sealed record SecurityModuleDetail(Guid Id, string Code, string Name, string? Description, short SortOrder, bool IsActive, DateTimeOffset? DeletedAt);
public sealed record SecurityModuleSummary(Guid Id, string Code, string Name, string? Description, short SortOrder, bool IsActive, int PermissionCount);
public sealed record AccessibleModuleDto(Guid Id, string Code, string Name, short SortOrder, IReadOnlyList<AccessibleActionDto> Actions);
public sealed record AccessibleActionDto(Guid Id, string Slug, string Name);
