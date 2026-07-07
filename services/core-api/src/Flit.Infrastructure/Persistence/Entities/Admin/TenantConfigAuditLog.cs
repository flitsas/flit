namespace Flit.Infrastructure.Persistence.Entities.Admin;

/// <summary>
/// Bitácora de cambios de configuración — <c>admin.tenant_config_audit_logs</c>.
/// Una fila por campo modificado (HU #10190, AC1).
/// </summary>
public sealed class TenantConfigAuditLog
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

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
}
