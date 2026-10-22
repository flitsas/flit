namespace Flit.Modules.Security.Domain.PermissionOverrides;

/// <summary>Acciones de la bitácora que no escribe el flujo de overrides (HU #13647, AC4).</summary>
public static class PermissionAuditActions
{
    /// <summary>Confirmación Ley 1581 de <c>ict.pii.reveal</c> (HU #13653): se escribe junto al GRANT, en la misma transacción.</summary>
    public const string PiiRevealConfirmed = "PII_REVEAL_CONFIRMED";

    /// <summary>Slug del permiso que exige la confirmación (HU #13653).</summary>
    public const string PiiRevealSlug = "ict.pii.reveal";
}

/// <summary>Filtros ya resueltos por ámbito (<c>TenantId</c> es obligatorio salvo para el Super Admin).</summary>
public sealed record PermissionAuditFilter(
    Guid? TenantId, Guid? UserId, Guid? ActorId, DateTimeOffset? From, DateTimeOffset? To, int Page, int PageSize);

/// <summary>Registro de la bitácora con nombres legibles (JOIN, sin N+1). Fechas en UTC.</summary>
public sealed record PermissionAuditRecord(
    Guid Id,
    Guid TenantId,
    string? TenantName,
    Guid ActorUserId,
    string? ActorName,
    string? ActorEmail,
    Guid TargetUserId,
    string? TargetName,
    string? TargetEmail,
    string? ProductCode,
    string? ModuleCode,
    string? PermissionSlug,
    string Action,
    string Outcome,
    string? ReasonCode,
    string? ValueBefore,
    string? ValueAfter,
    Guid? BatchId,
    DateTimeOffset CreatedAt,
    string? PermissionName = null,
    string? ModuleName = null);

public sealed record PermissionAuditPage(IReadOnlyList<PermissionAuditRecord> Items, int Total, int Page, int PageSize);

/// <summary>Quién y cuándo otorgó un permiso individual activo (FD1-8).</summary>
public sealed record OverrideGrantInfo(Guid PermissionId, string? GrantedByName, DateTimeOffset GrantedAt);

/// <summary>El administrador pidió un tenant distinto al suyo (HU #13647, AC2): 403.</summary>
public sealed class PermissionAuditScopeException()
    : Exception("Solo puedes consultar la auditoría de tu propia compañía.");

public interface IPermissionAuditReader
{
    /// <summary>Página de la bitácora, created_at DESC, con filtros explícitos (la RLS no aplica al owner).</summary>
    Task<PermissionAuditPage> QueryAsync(PermissionAuditFilter filter, CancellationToken ct);

    /// <summary>Nombre de quien otorgó y fecha de cada override activo del usuario en el tenant (una sola consulta).</summary>
    Task<IReadOnlyList<OverrideGrantInfo>> GetGrantInfoAsync(Guid tenantId, Guid userId, CancellationToken ct);
}
