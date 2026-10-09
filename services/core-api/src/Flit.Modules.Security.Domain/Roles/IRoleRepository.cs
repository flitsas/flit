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

    /// <summary>
    /// Productos (<c>security.modules.product_code</c>) de los módulos a los que pertenecen esos permisos
    /// (HU #12964). Un rol solo puede tener permisos de su producto.
    /// </summary>
    Task<IReadOnlyList<string>> GetPermissionProductCodesAsync(IReadOnlyList<Guid> permissionIds, CancellationToken ct);

    // ── HU #13441: roles propios de un tenant (Admin de Compañía) ─────────────────────────────────

    /// <summary>
    /// Roles que ve un tenant para un tipo de entidad: los globales (<c>tenant_id</c> NULL) y los propios.
    /// Nunca los de otro tenant.
    /// </summary>
    Task<IReadOnlyList<RoleSummary>> ListVisibleToTenantAsync(Guid tenantId, string targetEntityType, CancellationToken ct);

    /// <summary>Detalle del rol si es global o propio del tenant; <c>null</c> si no existe o es de otro tenant.</summary>
    Task<RoleDetail?> GetVisibleToTenantAsync(Guid tenantId, Guid roleId, CancellationToken ct);

    /// <summary>
    /// <c>true</c> si el code ya está tomado para el tenant: lo usa un rol global vigente (cualquier tipo de entidad)
    /// o un rol propio vigente del tenant (HU #13440 AC2).
    /// </summary>
    Task<bool> CodeTakenForTenantAsync(Guid tenantId, string code, CancellationToken ct);

    /// <summary>Cambia nombre y descripción de un rol propio del tenant.</summary>
    Task UpdateDetailsAsync(Guid roleId, string name, string? description, CancellationToken ct);

    /// <summary>Slug y producto de cada permiso pedido (los que no existen, no vuelven).</summary>
    Task<IReadOnlyList<PermissionInfo>> GetPermissionInfosAsync(IReadOnlyList<Guid> permissionIds, CancellationToken ct);

    /// <summary>Productos encendidos para el tenant; <c>plataforma</c> siempre cuenta (fail-closed para el resto).</summary>
    Task<IReadOnlySet<string>> GetEnabledProductCodesAsync(Guid tenantId, CancellationToken ct);

    /// <summary>
    /// Permisos activos que el tenant puede ofrecer al armar un rol propio (los de módulos de productos encendidos),
    /// sin filtrar por lo que posea el caller (eso lo hace el handler).
    /// </summary>
    Task<IReadOnlyList<PermissionInfo>> ListGrantablePermissionsAsync(IReadOnlyCollection<string> productCodes, CancellationToken ct);
}

/// <summary>Un permiso con el producto de su módulo (HU #13441).</summary>
public sealed record PermissionInfo(Guid Id, string Slug, string Name, string ModuleCode, string ProductCode);

public sealed record CreateRoleData(
    string TargetEntityType,
    string Code,
    string Name,
    string? Description,
    string ProductCode = "tramites",
    Guid? TenantId = null);

public sealed record RoleDetail(
    Guid Id,
    string TargetEntityType,
    string Code,
    string Name,
    string? Description,
    bool IsSystem,
    bool IsActive,
    IReadOnlyList<PermissionSlug> Permissions,
    string ProductCode = "tramites",
    Guid? TenantId = null);

public sealed record PermissionSlug(Guid Id, string Slug, string Name);

public sealed record RoleSummary(
    Guid Id,
    string TargetEntityType,
    string Code,
    string Name,
    string? Description,
    bool IsSystem,
    bool IsActive,
    int PermissionCount,
    DateTimeOffset CreatedAt,
    string ProductCode = "tramites",
    Guid? TenantId = null);
