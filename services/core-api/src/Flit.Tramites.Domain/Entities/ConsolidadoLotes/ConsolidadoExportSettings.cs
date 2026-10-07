namespace Flit.Tramites.Domain.Entities.ConsolidadoLotes;

/// <summary>
/// HU #13367 (Feature #13306, ADR-0070 A3.4) — parámetros del motor de lotes (<c>tramites.consolidado_export_settings</c>).
/// Fila única global de plataforma (sin compañía), sembrada por la migración con los valores por defecto. Los rangos y
/// la coherencia lease &gt; timeout los impone la base con CHECK nombrados.
/// </summary>
public sealed class ConsolidadoExportSettings
{
    public Guid Id { get; set; }

    /// <summary>N: PDF por parte ZIP (1–5000, por defecto 500).</summary>
    public int MaxPdfsPerPart { get; set; }

    /// <summary>M: MB de PDF en claro por parte (10–2048, por defecto 250).</summary>
    public int MaxMbPerPart { get; set; }

    /// <summary>K: ítems concurrentes del motor por instancia (1–6, por defecto 2).</summary>
    public short ItemSlots { get; set; }

    public int ItemTimeoutSeconds { get; set; }

    /// <summary>Lease del reclamo de ítem; siempre mayor que <see cref="ItemTimeoutSeconds"/>.</summary>
    public int ItemLeaseSeconds { get; set; }

    public short MaxItemAttempts { get; set; }

    /// <summary>Espera antes de reintentar un ítem por error técnico (mínimo 5 s, por defecto 30).</summary>
    public int RetryDelaySeconds { get; set; }

    public int PartTimeoutSeconds { get; set; }

    /// <summary>Lease del empaquetado de una parte; siempre mayor que <see cref="PartTimeoutSeconds"/>.</summary>
    public int PartLeaseSeconds { get; set; }

    public short MaxPartAttempts { get; set; }

    /// <summary>Horas que se conservan las partes tras terminar el lote (1–168, por defecto 24).</summary>
    public int RetentionHours { get; set; }

    /// <summary>Interruptor del motor.</summary>
    public bool IsActive { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }

    /// <summary>Token de concurrencia; lo incrementa el trigger <c>tr_consolidado_export_settings_row_version</c>.</summary>
    public long RowVersion { get; set; }
}
