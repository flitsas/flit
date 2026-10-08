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

    /// <summary>Valor sembrado de <see cref="MaxItemsPerBatch"/> (DEFAULT del DDL 133).</summary>
    public const int MaxItemsPerBatchPorDefecto = 10_000;

    /// <summary>
    /// Code review épica #13216 (Obs1) — máximo de <see cref="MaxItemsPerBatch"/> que admite
    /// <c>ck_consolidado_export_settings_max_items</c>: <c>short.MaxValue - 1</c>. El número de parte es
    /// <c>smallint</c> y el reparto puede dar UNA parte por ítem con independencia de N (<c>max_pdfs_per_part</c>): un
    /// PDF mayor que M (<c>max_mb_per_part</c>, desde 10 MB) va solo, y dos que juntos superan M también. Cada parte
    /// lleva al menos un ítem propio (los omitidos tardíos incluidos) salvo la parte 0/0 de un lote sin partes, que es
    /// como mucho una. Así un lote tiene a lo sumo <c>ítems + 1</c> partes, y con este máximo nunca pasa de 32.767.
    /// </summary>
    public const int MaxItemsPerBatchMaximo = short.MaxValue - 1;

    /// <summary>
    /// M1 (épica #13216) — tope total de trámites de un lote, en todos los orígenes y en los dos modos de selección
    /// (1–<see cref="MaxItemsPerBatchMaximo"/>, por defecto 10.000). Se compara con la selección ya resuelta (exclusiones e intersección de seguridad
    /// aplicadas); superarlo es <c>422 seleccion_excede_tope</c> y no se crea nada.
    /// </summary>
    public int MaxItemsPerBatch { get; set; } = MaxItemsPerBatchPorDefecto;

    public DateTimeOffset CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }

    /// <summary>Token de concurrencia; lo incrementa el trigger <c>tr_consolidado_export_settings_row_version</c>.</summary>
    public long RowVersion { get; set; }
}
