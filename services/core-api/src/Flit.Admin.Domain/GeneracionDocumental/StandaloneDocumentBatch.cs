namespace Flit.Admin.Domain.GeneracionDocumental;

/// <summary>
/// Cabecera de un lote XLSX de generación documental — <c>admin.standalone_document_batches</c>
/// (Feature #12201, incremento I3, DDL <c>106-F12201-generacion-documental-lotes.sql</c>).
///
/// <para><b>No hay tabla de items</b> (sub-decisión §3.c del diseño): una fila del XLSX ES una fila
/// de <c>admin.standalone_documents</c> vinculada por (<c>batch_id</c>, <c>row_number</c>). Por eso
/// aquí solo viven la metadata del archivo fuente, los contadores y la marca del reaper.</para>
///
/// <para>El XLSX fuente vive en storage, igual que los PDF: <b>prohibido <c>BYTEA</c></b>. De él se
/// guardan nombre, ruta opaca y SHA-256.</para>
/// </summary>
public sealed class StandaloneDocumentBatch
{
    public Guid Id { get; init; }

    /// <summary>Tenant del JWT. Es el aislamiento efectivo (la RLS de la tabla es decorativa).</summary>
    public Guid TenantId { get; init; }

    /// <summary>Autor de la carga (<c>created_by_user_id</c>, columna de negocio con FK).</summary>
    public Guid CreatedByUserId { get; init; }

    /// <summary>Uno de <see cref="StandaloneDocumentBatchStatus"/>.</summary>
    public string Status { get; init; } = StandaloneDocumentBatchStatus.Queued;

    /// <summary>Versión de la plantilla aceptada. Hoy solo existe <c>v1</c>.</summary>
    public string TemplateVersion { get; init; } = "v1";

    public string SourceFilename { get; init; } = string.Empty;

    /// <summary>Ruta opaca del XLSX en storage. Contiene los datos completos: no se expone en listados.</summary>
    public string SourceStoragePath { get; init; } = string.Empty;

    public string SourceSha256 { get; init; } = string.Empty;

    /// <summary>Filas de datos del XLSX, sin contar el encabezado. Tope duro de 100 (CHECK en BD).</summary>
    public int TotalItems { get; init; }

    public int GeneratedCount { get; init; }

    public int ErrorCount { get; init; }

    /// <summary>Clave de idempotencia por tenant (CF-16). <c>null</c> si el cliente no la envió.</summary>
    public string? IdempotencyKey { get; init; }

    /// <summary>Marca de toma por el worker. Base del reaper de lotes atascados (R5).</summary>
    public DateTimeOffset? ClaimedAt { get; init; }

    public DateTimeOffset? CompletedAt { get; init; }

    public DateTimeOffset CreatedAt { get; init; }
}

/// <summary>
/// Los cinco estados de <c>admin.standalone_document_batches.status</c> (CHECK
/// <c>ck_standalone_document_batches_status</c>). Los tres terminales EXIGEN <c>completed_at</c>:
/// el CHECK <c>ck_standalone_document_batches_cierre</c> rechaza un lote cerrado sin fecha de cierre.
/// </summary>
public static class StandaloneDocumentBatchStatus
{
    /// <summary>Cargado y en cola. Ningún documento generado todavía.</summary>
    public const string Queued = "queued";

    /// <summary>Reclamado por el worker (<c>claimed_at</c> fijado).</summary>
    public const string Processing = "processing";

    /// <summary>Terminó y TODAS las filas quedaron en <c>generated</c>.</summary>
    public const string Completed = "completed";

    /// <summary>Terminó con filas generadas y filas en error (CF-13). No es un fallo del lote.</summary>
    public const string PartialFailure = "partial_failure";

    /// <summary>Terminó y NINGUNA fila se generó.</summary>
    public const string Failed = "failed";

    public static bool IsTerminal(string? status) =>
        status is Completed or PartialFailure or Failed;
}

/// <summary>
/// Un error de una fila del XLSX (CF-13). Va a <c>validation_errors</c> como
/// <c>[{ code, field, message }]</c>.
///
/// <para><b><see cref="Message"/> NUNCA refleja el valor capturado</b>: la fila puede traer un
/// documento de identidad o una dirección y el listado de errores lo enseñaría a cualquiera con
/// permiso de lectura. Se nombra el campo, no su contenido.</para>
/// </summary>
public sealed record StandaloneDocumentValidationError(string Code, string Field, string Message);
