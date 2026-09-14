namespace Flit.Tramites.Domain.Entities.BulkTramites;

/// <summary>Estado del lote de carga masiva. El procesamiento fila a fila lo lleva HU #12523.</summary>
public static class BulkTramitesBatchStatus
{
    public const string Queued = "queued";
    public const string Processing = "processing";
    public const string Completed = "completed";
}

/// <summary>
/// Cabecera de un lote de carga masiva de trámites (HU #12522, Feature #12519) —
/// <c>tramites.bulk_tramites_batches</c>. Una fila por archivo Excel subido; el detalle fila a
/// fila vive en <see cref="BulkTramitesBatchRow"/>.
///
/// <para>No se guarda el XLSX fuente: a diferencia de Generación Documental (que sí lo necesita
/// para auditoría del documento emitido), aquí cada fila ya queda parseada en
/// <see cref="BulkTramitesBatchRow.ValuesJson"/> y es lo único que el procesador de HU #12523
/// necesita leer.</para>
/// </summary>
public sealed class BulkTramitesBatch
{
    public Guid Id { get; set; }

    /// <summary>Tenant del JWT del usuario que subió el archivo.</summary>
    public Guid TenantId { get; set; }

    public Guid CreatedByUserId { get; set; }

    /// <summary>Uno de los valores de <c>BulkTramitesTemplateType</c> (Matricula/Traspaso/Otros), en minúsculas.</summary>
    public string TemplateType { get; set; } = string.Empty;

    /// <summary>Uno de <see cref="BulkTramitesBatchStatus"/>.</summary>
    public string Status { get; set; } = BulkTramitesBatchStatus.Queued;

    public string SourceFilename { get; set; } = string.Empty;

    /// <summary>Filas de datos del archivo (sin contar el encabezado). Tope duro de 50 (CHECK en BD).</summary>
    public int TotalRows { get; set; }

    /// <summary>
    /// Filas que ya quedaron en error al parsear (p. ej. porcentajes de propiedad que no suman
    /// 100): no entran a la cola de procesamiento de HU #12523.
    /// </summary>
    public int RowsWithStructuralErrors { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }

    public ICollection<BulkTramitesBatchRow> Rows { get; set; } = [];
}
