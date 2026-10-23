namespace Flit.Infrastructure.Persistence.Entities.Admin;

/// <summary>
/// Bitácora de cambios de configuración — <c>admin.tenant_config_audit_logs</c>.
/// Una fila por campo modificado (HU #10190, AC1). HU #10678 generaliza esta misma tabla
/// a rastro único de auditoría administrativa/seguridad (usuarios, roles, permisos,
/// autenticación) — ver columnas nuevas más abajo.
/// </summary>
public sealed class TenantConfigAuditLog
{
    public Guid Id { get; set; }

    /// <summary>
    /// Tenant al que pertenece la fila. Nullable (HU #10678): eventos de autenticación sin
    /// tenant resoluble (p. ej. login fallido con email inexistente) se auditan igual.
    /// </summary>
    public Guid? TenantId { get; set; }

    public string EntityName { get; set; } = string.Empty;

    public string FieldName { get; set; } = string.Empty;

    /// <summary>Valor anterior serializado como JSON (jsonb). Nullable.</summary>
    public string? OldValue { get; set; }

    /// <summary>Valor nuevo serializado como JSON (jsonb). Nullable.</summary>
    public string? NewValue { get; set; }

    public DateTimeOffset ChangedAt { get; set; }

    public Guid? ChangedBy { get; set; }

    public Guid? CorrelationId { get; set; }

    // ── Auditoría mínima RNF01 (ADR-0024) ──────────────────────────────────────────
    // Columnas aditivas nullable: sin backfill de filas históricas.

    /// <summary>IP de origen (respeta X-Forwarded-For). Nullable: filas históricas / sin HTTP.</summary>
    public string? ClientIp { get; set; }

    /// <summary>Desenlace: <c>success</c> | <c>failure</c> (ver <c>AuditVocabulary.Results</c>). Nullable.</summary>
    public string? Result { get; set; }

    /// <summary>Operación explícita: <c>create</c> | <c>update</c> | <c>delete</c>. Nullable.</summary>
    public string? Operation { get; set; }

    /// <summary>Código de error estable en filas <c>failure</c> (sin datos sensibles). Nullable.</summary>
    public string? ErrorCode { get; set; }

    // ── Auditoría administrativa/seguridad transversal (HU #10678) ─────────────────────
    // Columnas aditivas nullable: sin backfill de filas históricas de config.

    /// <summary>Tipo de tenant denormalizado: <c>COMPANY</c> | <c>TRANSIT_OFFICE</c>. Nullable.</summary>
    public string? TenantType { get; set; }

    /// <summary>
    /// Categoría de la operación auditada: <c>users</c> | <c>roles</c> | <c>permissions</c> |
    /// <c>authentication</c> | <c>security</c> | <c>config</c> (ver <c>AuditVocabulary.Modules</c>).
    /// </summary>
    public string? Module { get; set; }

    /// <summary>Tipo de entidad afectada: <c>USER</c> | <c>ROLE</c> | <c>INVITATION</c> | … Nullable.</summary>
    public string? TargetEntityType { get; set; }

    /// <summary>Id de la entidad afectada (usuario/rol/invitación objetivo). Nullable.</summary>
    public Guid? TargetEntityId { get; set; }

    /// <summary>User-Agent del cliente que originó la petición. Nullable.</summary>
    public string? UserAgent { get; set; }
}
