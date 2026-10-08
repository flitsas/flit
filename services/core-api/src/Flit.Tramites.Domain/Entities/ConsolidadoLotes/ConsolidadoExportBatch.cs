namespace Flit.Tramites.Domain.Entities.ConsolidadoLotes;

/// <summary>
/// HU #13367 (Feature #13306, ADR-0070) — cabecera de un lote de descarga masiva de consolidados en ZIP
/// (<c>tramites.consolidado_export_batches</c>). Las invariantes de la máquina de estados (terminal ⇔ <c>finished_at</c>,
/// activo ⇒ DEK viva, purga ⇒ DEK nula), las de origen y el «un lote activo por usuario» las garantiza la base
/// (DDL 133). Las partes, los ítems y la auditoría llegan con HU #13368.
/// </summary>
public sealed class ConsolidadoExportBatch
{
    public Guid Id { get; set; }

    /// <summary>
    /// Compañía del solicitante. <c>null</c> si y solo si <see cref="Origin"/> es
    /// <see cref="ConsolidadoExportOrigin.Superadmin"/> (E5, Q8). Las consultas del dueño no filtran por este valor
    /// sino por <see cref="RequestedByUserId"/>.
    /// </summary>
    public Guid? TenantId { get; set; }

    /// <summary>Usuario solicitante (<c>sub</c>): dueño del lote.</summary>
    public Guid RequestedByUserId { get; set; }

    /// <summary>Rol con el que se creó el lote (revalidación CF-16 y auditoría).</summary>
    public string RequestedRoleCode { get; set; } = string.Empty;

    /// <summary>
    /// Compañía a la que se acotó la selección: en origen <see cref="ConsolidadoExportOrigin.Superadmin"/>, la de
    /// <c>X-Tenant-Id</c>; en un lote de red (<see cref="NetworkScope"/>, HU #13417), la hija acotada (nunca la propia
    /// compañía del lote). <c>null</c> = sin acotar (todas, o toda la red).
    /// </summary>
    public Guid? ScopeTenantId { get; set; }

    /// <summary>
    /// HU #13417 (ADR-0070 adenda v7) — lote creado desde la vista de red de una cabeza: solo origen
    /// <see cref="ConsolidadoExportOrigin.Tramites"/>, con <see cref="TenantId"/> = cabeza y cada ítem en la compañía de
    /// su trámite. Lo garantizan los CHECK <c>network_origin</c>, <c>network_child</c> y <c>scope_origin</c> del DDL 133.
    /// </summary>
    public bool NetworkScope { get; set; }

    /// <summary>Uno de <see cref="ConsolidadoExportOrigin"/>.</summary>
    public string Origin { get; set; } = ConsolidadoExportOrigin.Tramites;

    /// <summary>Uno de <see cref="ConsolidadoExportDocumentType"/>.</summary>
    public string DocumentType { get; set; } = ConsolidadoExportDocumentType.Consolidado;

    /// <summary>Uno de <see cref="ConsolidadoExportSelectionMode"/>.</summary>
    public string SelectionMode { get; set; } = ConsolidadoExportSelectionMode.Ids;

    /// <summary>Uno de <see cref="ConsolidadoExportStatus"/>.</summary>
    public string Status { get; set; } = ConsolidadoExportStatus.EnCola;

    /// <summary>Total congelado al crear el lote.</summary>
    public int TotalItems { get; set; }

    public int IncludedCount { get; set; }

    public int OmittedCount { get; set; }

    /// <summary>Incluidos que se generaron por primera vez (<c>&lt;= IncludedCount</c>).</summary>
    public int GeneratedCount { get; set; }

    public short PartsCount { get; set; }

    /// <summary>Clave de datos del lote envuelta. <c>null</c> tras la purga o la cancelación (borrado criptográfico).</summary>
    public byte[]? DekWrapped { get; set; }

    /// <summary>Instante en que el usuario aceptó el texto de confirmación de efectos (CF-08).</summary>
    public DateTimeOffset EffectsAcknowledgedAt { get; set; }

    /// <summary>Último reclamo de un ítem del lote (round-robin entre lotes, CF-21).</summary>
    public DateTimeOffset? LastClaimedAt { get; set; }

    public DateTimeOffset? StartedAt { get; set; }

    public DateTimeOffset? FinishedAt { get; set; }

    public DateTimeOffset? ExpiresAt { get; set; }

    public DateTimeOffset? PurgedAt { get; set; }

    public string? ErrorCode { get; set; }

    /// <summary>Obligatorio si y solo si <see cref="Origin"/> es <see cref="ConsolidadoExportOrigin.OtBandeja"/> (R-d).</summary>
    public Guid? OtTransitOfficeId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public Guid CreatedBy { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }

    public DateTimeOffset? DeletedAt { get; set; }

    public Guid? DeletedBy { get; set; }

    /// <summary>Token de concurrencia; lo incrementa el trigger <c>tr_consolidado_export_batches_row_version</c>.</summary>
    public long RowVersion { get; set; }
}
