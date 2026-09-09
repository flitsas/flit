namespace Flit.Infrastructure.Persistence.Entities.Admin;

/// <summary>
/// Documento generado SIN trámite — <c>admin.standalone_documents</c> (Feature #12201,
/// ADR-0056-generacion-documental-standalone). El PDF vive en storage: aquí solo metadata, hash,
/// snapshot reproducible y auditoría de descarga. <b>Prohibido <c>BYTEA</c></b> (contraste
/// deliberado con <c>admin.impronta_generations</c>, que sí lo usa y no se replica) y <b>ninguna FK
/// hacia <c>tramites.*</c></b>.
///
/// <para><b>Nombres de columna verificados contra el DDL 105</b>, que es la fuente de verdad:
/// <c>created_by_user_id</c> (NO <c>flit_user_id</c>), <c>scenario</c> y <c>storage_sha256</c> (NO
/// <c>sha256</c>). Un desalineamiento aquí no rompe la compilación: revienta en runtime.</para>
/// </summary>
public sealed class StandaloneDocumentEntity
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    /// <summary>
    /// Autor de la generación. Columna de negocio con FK a <c>identity.users</c>, distinta de la
    /// columna de auditoría <see cref="CreatedBy"/>. Misma convención que
    /// <c>tramites.procedure_instances.created_by_user_id</c>.
    /// </summary>
    public Guid CreatedByUserId { get; set; }

    public string DocumentType { get; set; } = string.Empty;

    /// <summary>Escenario normativo A/B/C. <c>null</c> en el Certificado RUES (CHECK por tipo).</summary>
    public string? Scenario { get; set; }

    public string Status { get; set; } = "pending";

    public string? ErrorCode { get; set; }

    public string? ErrorField { get; set; }

    public string? StoragePath { get; set; }

    /// <summary>SHA-256 del binario en hex minúsculas — columna <c>storage_sha256</c>.</summary>
    public string? StorageSha256 { get; set; }

    public long? SizeBytes { get; set; }

    public string? Filename { get; set; }

    public string? IdempotencyKey { get; set; }

    /// <summary>jsonb pobre en PII para el listado del historial.</summary>
    public string InputSummary { get; set; } = "{}";

    /// <summary>jsonb <c>{ queriedAt, fields }</c>. Inmutable una vez escrito (trigger, capa 1).</summary>
    public string? RuesSnapshot { get; set; }

    /// <summary>jsonb con el payload completo del generador de transferencia (CF-26, PII alta).</summary>
    public string? DocumentSnapshot { get; set; }

    /// <summary>Fecha de la ÚLTIMA descarga (CF-19). Exenta del trigger de inmutabilidad.</summary>
    public DateTimeOffset? DownloadedAt { get; set; }

    /// <summary>Contador de auditoría de descargas (CF-19). No es un cupo: no limita descargas.</summary>
    public int DownloadCount { get; set; }

    /// <summary>
    /// Lote XLSX de origen (DDL 106). <c>null</c> en la generación individual. INMUTABLE: la capa 2
    /// del trigger rechaza cualquier UPDATE que lo toque, incluido pasarlo de nulo a un valor.
    /// </summary>
    public Guid? BatchId { get; set; }

    /// <summary>Número de fila en el XLSX (1-based, sin encabezado). Inmutable, como <see cref="BatchId"/>.</summary>
    public int? RowNumber { get; set; }

    /// <summary>
    /// jsonb <c>[{ code, field, message }]</c> con los errores de la fila del XLSX (CF-13). NOT NULL
    /// con default <c>[]</c>. Exenta del trigger de inmutabilidad, igual que <c>downloaded_at</c>.
    /// </summary>
    public string ValidationErrors { get; set; } = "[]";

    public long RowVersion { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }

    public DateTimeOffset? DeletedAt { get; set; }

    public Guid? DeletedBy { get; set; }
}
