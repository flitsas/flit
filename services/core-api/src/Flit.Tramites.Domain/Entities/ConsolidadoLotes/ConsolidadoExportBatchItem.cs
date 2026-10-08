namespace Flit.Tramites.Domain.Entities.ConsolidadoLotes;

/// <summary>
/// HU #13368 (Feature #13306, ADR-0070) — un trámite dentro de un lote (<c>tramites.consolidado_export_batch_items</c>).
/// Un trámite no se repite en el mismo lote (<c>uq_consolidado_export_batch_items_batch_instance</c>). La coherencia por
/// estado (incluido ⇒ snapshot completo y modo de entrega; omitido ⇔ código del vocabulario
/// <see cref="ConsolidadoLoteOmisiones"/>; vivo ⇒ sin parte) la garantiza la base (DDL 134).
/// </summary>
public sealed class ConsolidadoExportBatchItem
{
    public Guid Id { get; set; }

    /// <summary>Compañía DEL TRÁMITE (CF-15), no la del solicitante. Siempre presente, también en lotes de Super Admin.</summary>
    public Guid TenantId { get; set; }

    public Guid BatchId { get; set; }

    public Guid ProcedureInstanceId { get; set; }

    /// <summary>Orden del ítem en la selección (base del reclamo dentro del lote).</summary>
    public int Position { get; set; }

    /// <summary>Uno de <see cref="ConsolidadoExportItemStatus"/>.</summary>
    public string Status { get; set; } = ConsolidadoExportItemStatus.Pendiente;

    public short Attempts { get; set; }

    public DateTimeOffset NextAttemptAt { get; set; }

    public DateTimeOffset? LeaseUntil { get; set; }

    /// <summary>Instancia/slot que reclamó el ítem (diagnóstico).</summary>
    public string? ClaimedBy { get; set; }

    /// <summary>Radicado del trámite (snapshot al crear el lote).</summary>
    public string ReferenceNumber { get; set; } = string.Empty;

    /// <summary>
    /// Placa (dato personal indirecto, <c>@pii:low</c>). Nombra el PDF y el CSV de omitidos; la purga la conserva.
    /// Nunca en logs.
    /// </summary>
    public string? Plate { get; set; }

    public Guid? AttachmentId { get; set; }

    /// <summary>Snapshot del id opaco del PDF entregado en el file-manager.</summary>
    public string? StoragePath { get; set; }

    public long? SizeBytes { get; set; }

    /// <summary>Uno de <see cref="ConsolidadoExportDeliveryMode"/>; obligatorio si el ítem está incluido.</summary>
    public string? DeliveryMode { get; set; }

    /// <summary>Uno de <see cref="ConsolidadoLoteOmisiones"/>; presente si y solo si el ítem está omitido.</summary>
    public string? OmissionCode { get; set; }

    /// <summary>Texto legible del motivo para el CSV de omitidos (sin datos personales).</summary>
    public string? OmissionReason { get; set; }

    /// <summary>Parte a la que se asignó el ítem (FK compuesta <c>(BatchId, PartNumber)</c>).</summary>
    public short? PartNumber { get; set; }

    public DateTimeOffset? ProcessedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Solicitante del lote.</summary>
    public Guid CreatedBy { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }

    /// <summary>Token de concurrencia; lo incrementa <c>tr_consolidado_export_batch_items_row_version</c>.</summary>
    public long RowVersion { get; set; }
}
