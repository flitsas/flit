namespace Flit.Tramites.Domain.Entities.BulkTramites;

/// <summary>Resultado de una fila tras el procesamiento de HU #12523. Null mientras está en cola.</summary>
public static class BulkTramitesRowOutcome
{
    /// <summary>Vehículo y actores consultados sin problema.</summary>
    public const string Created = "created";

    /// <summary>Vehículo consultado con éxito pero algún actor falló: el trámite se crea igual.</summary>
    public const string CreatedPending = "created_pending";

    /// <summary>La consulta del vehículo falló: no se creó el trámite.</summary>
    public const string NotCreated = "not_created";
}

/// <summary>
/// Una fila del archivo de carga masiva, ya parseada — <c>tramites.bulk_tramites_batch_rows</c>
/// (HU #12522). <see cref="RowNumber"/> es 1-based y no cuenta el encabezado: es el número que ve
/// el usuario en el resumen del lote (HU #12524).
///
/// <para><see cref="ValuesJson"/> guarda los valores tal cual vinieron en el Excel, indexados por
/// el encabezado de la plantilla (no por posición): una celda vacía se omite del XML y contar
/// posiciones desalinearía la fila entera.</para>
///
/// <para><see cref="StructuralErrorCode"/> es el único campo que llena el parser de HU #12522 (p. ej.
/// <c>porcentajes_no_suman_100</c>). <see cref="Outcome"/>, <see cref="OutcomeReason"/> y
/// <see cref="ProcedureInstanceId"/> los llena el procesador asíncrono de HU #12523 — una fila con
/// error estructural nunca entra a esa cola.</para>
/// </summary>
public sealed class BulkTramitesBatchRow
{
    public Guid Id { get; set; }
    public Guid BatchId { get; set; }
    public int RowNumber { get; set; }

    /// <summary>Valores de la fila, serializados como JSON (jsonb), indexados por encabezado de columna.</summary>
    public string ValuesJson { get; set; } = "{}";

    /// <summary>Código de error detectado al parsear (HU #12522), o null si la fila es estructuralmente válida.</summary>
    public string? StructuralErrorCode { get; set; }

    /// <summary>Uno de <see cref="BulkTramitesRowOutcome"/>, o null mientras no se ha procesado (HU #12523).</summary>
    public string? Outcome { get; set; }

    /// <summary>Motivo legible del resultado (p. ej. por qué falló la consulta del vehículo).</summary>
    public string? OutcomeReason { get; set; }

    /// <summary>Trámite creado por esta fila, si aplica.</summary>
    public Guid? ProcedureInstanceId { get; set; }

    public DateTimeOffset? ProcessedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
