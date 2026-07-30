namespace Flit.Infrastructure.Persistence.Entities.Analytics;

/// <summary>
/// Fuente durable de export jobs asíncronos — <c>analytics.export_jobs</c>.
/// (Feature #11076, ADR-0037.)
/// El worker <c>ExportJobsWorker</c> consulta esta tabla con <c>FOR UPDATE SKIP LOCKED</c>
/// para procesar un job a la vez por réplica. El trigger <c>tr_export_jobs_notify</c>
/// emite NOTIFY 'export_jobs_channel' al insertar para despertar al
/// <c>ExportJobsChannelListener</c> (fallback: polling cada 30 s).
/// </summary>
public sealed class ExportJob
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    /// <summary>Usuario propietario del job. Valida ownership en download-url.</summary>
    public Guid OwnerUserId { get; set; }

    /// <summary>pending | processing | completed | failed. Allowlist en CHECK constraint.</summary>
    public string Status { get; set; } = "pending";

    /// <summary>procedures | consolidado | productivity | sla. Allowlist en CHECK constraint.</summary>
    public string ReportType { get; set; } = string.Empty;

    /// <summary>excel | csv | pdf. Allowlist en CHECK constraint.</summary>
    public string Format { get; set; } = string.Empty;

    /// <summary>Filtros del reporte serializados. El backend los reaplica al procesar.</summary>
    public string FiltersJson { get; set; } = "{}";

    /// <summary>0–100. Actualizado por el worker vía SignalR push cada ~20%.</summary>
    public short ProgressPct { get; set; }

    /// <summary>X-Correlation-Id del request origen (trazabilidad distribuida §8.3).</summary>
    public Guid? CorrelationId { get; set; }

    /// <summary>ID opaco del file-manager (no URL). NULL hasta status=completed.</summary>
    public string? FileStoragePath { get; set; }

    public long? FileSizeBytes { get; set; }

    /// <summary>SHA-256 del archivo generado para integridad en descarga.</summary>
    public string? FileSha256 { get; set; }

    public string? ErrorMessage { get; set; }

    /// <summary>created_at + 30 días. Cron marca deleted_at al vencer.</summary>
    public DateTimeOffset ExpiresAt { get; set; }

    public DateTimeOffset? StartedAt { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }

    // ── Columnas estándar A5 ──────────────────────────────────────────────
    public long RowVersion { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public Guid? DeletedBy { get; set; }
}
