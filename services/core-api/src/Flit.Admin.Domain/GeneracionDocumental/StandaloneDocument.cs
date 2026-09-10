namespace Flit.Admin.Domain.GeneracionDocumental;

/// <summary>
/// Un intento de generación de documento SIN trámite — <c>admin.standalone_documents</c>
/// (Feature #12201, ADR-0056-generacion-documental-standalone). El PDF vive en storage; aquí
/// quedan metadata, integridad (<see cref="StorageSha256"/>), snapshot reproducible y auditoría de
/// descarga. <b>Ninguna FK hacia <c>tramites.*</c></b>: el documento no tiene ni referencia una
/// <c>procedure_instance</c> (CF-02).
/// </summary>
public sealed class StandaloneDocument
{
    public Guid Id { get; init; }

    /// <summary>Tenant del JWT. Es el aislamiento efectivo (la RLS del repo es decorativa).</summary>
    public Guid TenantId { get; init; }

    /// <summary>
    /// Autor de la generación (<c>created_by_user_id</c>, NO <c>flit_user_id</c>): columna de
    /// negocio con FK a <c>identity.users</c>, distinta de la de auditoría <c>created_by</c>.
    /// </summary>
    public Guid CreatedByUserId { get; init; }

    /// <summary>Uno de <see cref="StandaloneDocumentType"/>.</summary>
    public string DocumentType { get; init; } = StandaloneDocumentType.CertificadoRues;

    /// <summary>
    /// Escenario normativo A/B/C de la transferencia. <c>null</c> en el Certificado RUES: el CHECK
    /// <c>ck_standalone_documents_scenario_por_tipo</c> lo impone en base de datos.
    /// </summary>
    public string? Scenario { get; init; }

    /// <summary>Uno de <see cref="StandaloneDocumentStatus"/>.</summary>
    public string Status { get; init; } = StandaloneDocumentStatus.Pending;

    public string? ErrorCode { get; init; }

    public string? ErrorField { get; init; }

    public string? StoragePath { get; init; }

    /// <summary>SHA-256 del binario en hex minúsculas (<c>storage_sha256</c>, NO <c>sha256</c>).</summary>
    public string? StorageSha256 { get; init; }

    public long? SizeBytes { get; init; }

    public string? Filename { get; init; }

    /// <summary>Clave de idempotencia por tenant (CF-16). <c>null</c> si el cliente no la envió.</summary>
    public string? IdempotencyKey { get; init; }

    /// <summary>
    /// Resumen POBRE EN PII para el listado (Ley 1581): NIT/placa/escenario. Nunca domicilios,
    /// correos ni nombres completos. JSON serializado.
    /// </summary>
    public string InputSummary { get; init; } = "{}";

    /// <summary>Snapshot congelado de la consulta RUES (<c>{ queriedAt, fields }</c>, patrón ADR-0037).</summary>
    public string? RuesSnapshot { get; init; }

    /// <summary>Payload completo que alimentó al generador de transferencia (CF-26, PII alta).</summary>
    public string? DocumentSnapshot { get; init; }

    public DateTimeOffset? DownloadedAt { get; init; }

    public int DownloadCount { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>
    /// Lote XLSX de origen (Feature #12201, I3). <c>null</c> en una generación individual. El CHECK
    /// <c>ck_standalone_documents_batch_row</c> exige que viaje junto con <see cref="RowNumber"/>:
    /// una fila de lote sin número no es rastreable hasta el archivo, y un número sin lote no
    /// significa nada.
    /// <para>Ambas columnas son INMUTABLES desde el DDL 106 (capa 2 del trigger): no se pueden
    /// asignar después de insertar la fila. Quien crea la fila decide si es de lote o no.</para>
    /// </summary>
    public Guid? BatchId { get; init; }

    /// <summary>Número de fila en el XLSX, 1-based y sin contar el encabezado (CF-13).</summary>
    public int? RowNumber { get; init; }

    /// <summary>
    /// Errores de la fila del XLSX (CF-13) como <c>[{ code, field, message }]</c>. Vacío
    /// (<c>[]</c>) mientras no haya ninguno — la columna es <c>NOT NULL DEFAULT '[]'</c>.
    /// <para>Nunca refleja el valor capturado que produjo el error.</para>
    /// </summary>
    public string ValidationErrors { get; init; } = "[]";
}

/// <summary>Binario ya persistido en storage: lo que cierra la fila en <c>generated</c>.</summary>
public sealed record StandaloneDocumentFile(
    string StoragePath,
    string StorageSha256,
    long SizeBytes,
    string Filename);
