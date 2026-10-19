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
}
