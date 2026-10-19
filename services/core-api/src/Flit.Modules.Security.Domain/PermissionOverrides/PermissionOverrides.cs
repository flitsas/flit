namespace Flit.Modules.Security.Domain.PermissionOverrides;

/// <summary>Efecto de un permiso individual (ADR-0071): <c>allow</c> suma al rol, <c>deny</c> le resta.</summary>
public static class PermissionEffects
{
    public const string Allow = "allow";

    public const string Deny = "deny";

    public static bool IsValid(string? effect) => effect is Allow or Deny;
}

/// <summary>Origen de un override: puntual o parte de la foto de un módulo completo.</summary>
public static class PermissionOverrideSources
{
    public const string Individual = "INDIVIDUAL";

    public const string ModuleSnapshot = "MODULE_SNAPSHOT";
}

/// <summary>Permiso individual de un usuario en un tenant y producto (<c>security.user_permission_overrides</c>).</summary>
public sealed record UserPermissionOverride(
    Guid Id,
    Guid TenantId,
    Guid UserId,
    string ProductCode,
    Guid PermissionId,
    string Effect,
    string Source,
    Guid? BatchId,
    Guid GrantedBy);

/// <summary>Fila de la bitácora append-only (<c>security.user_permission_audit</c>); <c>Outcome</c> = OK | REJECTED.</summary>
public sealed record UserPermissionAuditEntry(
    Guid TenantId,
    Guid ActorUserId,
    Guid TargetUserId,
    string? ProductCode,
    string? ModuleCode,
    string? PermissionSlug,
    string Action,
    string Outcome,
    string? ReasonCode,
    string? ValueBefore,
    string? ValueAfter,
    Guid? BatchId);

/// <summary>Acción (permiso del catálogo) de un módulo, tal como existe en este momento.</summary>
public sealed record ModuleActionRef(Guid PermissionId, string Slug);

/// <summary>Módulo con su producto y sus acciones activas al momento de consultar (la "foto").</summary>
public sealed record ModuleActionsSnapshot(string ModuleCode, string ProductCode, IReadOnlyList<ModuleActionRef> Actions);

public sealed class ModuleNotFoundForOverrideException(string moduleCode)
    : Exception($"El módulo '{moduleCode}' no existe o no está activo.")
{
    public string ModuleCode { get; } = moduleCode;
}

public sealed class DuplicateUserPermissionOverrideException()
    : Exception("El usuario ya tiene un permiso individual para ese permiso en el tenant.");

public interface IUserPermissionOverrideRepository
{
    /// <summary>Acciones activas hoy del módulo, o <c>null</c> si el módulo no existe o está inactivo.</summary>
    Task<ModuleActionsSnapshot?> GetModuleActionsAsync(string moduleCode, CancellationToken ct);

    /// <summary>Overrides ACTIVOS del usuario en el tenant y producto.</summary>
    Task<IReadOnlyList<UserPermissionOverride>> ListActiveAsync(Guid tenantId, Guid userId, string productCode, CancellationToken ct);

    /// <summary>Inserta overrides y su auditoría en una sola transacción. Lanza <see cref="DuplicateUserPermissionOverrideException"/> por unicidad.</summary>
    Task AddAsync(
        Guid tenantId,
        IReadOnlyList<UserPermissionOverride> items,
        IReadOnlyList<UserPermissionAuditEntry> audit,
        CancellationToken ct);

    // HU #13644: tope del administrador y operaciones puntuales.

    /// <summary>Permiso del catálogo con su producto, módulo y marca <c>enforced</c>; <c>null</c> si no existe o está inactivo.</summary>
    Task<PermissionCatalogEntry?> GetPermissionAsync(Guid permissionId, CancellationToken ct);

    /// <summary>Permisos activos del catálogo con <c>enforced = 'true'</c> (los únicos otorgables).</summary>
    Task<IReadOnlyList<PermissionCatalogEntry>> ListEnforcedPermissionsAsync(CancellationToken ct);

    /// <summary>HU #13645: ids de los permisos activos con <c>enforced = 'excluded'</c> (nunca se otorgan ni se deniegan).</summary>
    Task<IReadOnlySet<Guid>> ListExcludedPermissionIdsAsync(CancellationToken ct);

    /// <summary>
    /// Ids de los permisos de los roles ACTIVOS del usuario en el tenant (unión), leídos de la BD en este momento.
    /// No incluye overrides individuales.
    /// </summary>
    Task<IReadOnlySet<Guid>> GetRolePermissionIdsAsync(Guid tenantId, Guid userId, CancellationToken ct);

    /// <summary>Overrides ACTIVOS del usuario en el tenant, de todos los productos.</summary>
    Task<IReadOnlyList<UserPermissionOverride>> ListActiveAsync(Guid tenantId, Guid userId, CancellationToken ct);

    /// <summary>
    /// En una sola transacción: da de baja (soft delete) los overrides indicados, inserta los nuevos y escribe la
    /// bitácora. Con listas vacías solo registra la auditoría (rechazos).
    /// </summary>
    Task ApplyAsync(
        Guid tenantId,
        Guid actorUserId,
        IReadOnlyList<Guid> removeOverrideIds,
        IReadOnlyList<UserPermissionOverride> add,
        IReadOnlyList<UserPermissionAuditEntry> audit,
        CancellationToken ct);
}

/// <summary>Permiso del catálogo. <c>Enforced</c>: 'true' | 'false' | 'excluded'.</summary>
public sealed record PermissionCatalogEntry(Guid Id, string Slug, string Name, string ModuleCode, string ProductCode, string Enforced)
{
    public bool IsEnforced => Enforced == "true";
}

/// <summary>Motivos de rechazo que quedan en <c>reason_code</c> de la bitácora (#13644 y #13645).</summary>
public static class PermissionOverrideReasons
{
    public const string OutOfCap = "OUT_OF_CAP";

    public const string NotEnforced = "NOT_ENFORCED";

    /// <summary>HU #13645: un administrador que no es Super Admin intenta configurar a otro administrador.</summary>
    public const string TargetIsAdmin = "TARGET_IS_ADMIN";

    /// <summary>HU #13645: el administrador intenta configurar sus propios permisos individuales.</summary>
    public const string Self = "SELF";

    /// <summary>HU #13645: el destino es Super Admin (nadie le configura permisos individuales).</summary>
    public const string TargetSuperadmin = "TARGET_SUPERADMIN";

    /// <summary>HU #13645: permiso con <c>enforced = 'excluded'</c> (no se configura jamás).</summary>
    public const string Excluded = "EXCLUDED";

    /// <summary>HU #13645: el usuario destino no pertenece al tenant en el que se opera (hija, otra compañía, otra OT).</summary>
    public const string OutOfScope = "OUT_OF_SCOPE";

    /// <summary>HU #13645: el usuario destino aún no aceptó su invitación.</summary>
    public const string InvitationPending = "INVITATION_PENDING";

    /// <summary>HU #13645: el usuario destino no existe.</summary>
    public const string UserNotFound = "USER_NOT_FOUND";
}

/// <summary>
/// HU #13645: lo que las reglas de ámbito necesitan saber de un usuario respecto a un tenant.
/// <c>IsMember</c> = asignación de rol vigente en el tenant o <c>home_tenant_id</c> igual al tenant.
/// </summary>
public sealed record OverrideTargetInfo(
    bool TenantExists,
    bool UserExists,
    bool IsMember,
    Guid? HomeTenantId,
    string Status,
    bool HasPendingInvitation,
    IReadOnlySet<string> RoleCodesInTenant,
    bool IsSuperAdmin);

public interface IOverrideTargetDirectory
{
    /// <summary>Estado del usuario respecto al tenant, con filtros explícitos por tenant (la app conecta como owner y la RLS no aplica).</summary>
    Task<OverrideTargetInfo> GetAsync(Guid tenantId, Guid userId, CancellationToken ct);
}

public sealed class PermissionNotFoundForOverrideException(Guid permissionId)
    : Exception($"El permiso '{permissionId}' no existe o no está activo.")
{
    public Guid PermissionId { get; } = permissionId;
}

/// <summary>La operación no cabe en el tope del administrador o el catálogo no la permite. Ya quedó auditada como REJECTED.</summary>
public sealed class PermissionOverrideRejectedException(string reasonCode, string slug)
    : Exception($"Operación rechazada ({reasonCode}) sobre '{slug}'.")
{
    public string ReasonCode { get; } = reasonCode;

    public string Slug { get; } = slug;
}
