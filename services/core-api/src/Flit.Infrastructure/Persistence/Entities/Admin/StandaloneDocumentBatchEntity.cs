namespace Flit.Infrastructure.Persistence.Entities.Admin;

/// <summary>
/// Cabecera de lote XLSX — <c>admin.standalone_document_batches</c> (Feature #12201, I3, DDL
/// <c>106-F12201-generacion-documental-lotes.sql</c>).
///
/// <para>El XLSX fuente vive en storage: aquí solo nombre, ruta opaca, hash y contadores.
/// <b>Prohibido <c>BYTEA</c></b>, igual que en <c>admin.standalone_documents</c>. Y <b>ninguna FK
/// hacia <c>tramites.*</c></b>.</para>
///
/// <para>Nombres de columna verificados contra el DDL 106: <c>created_by_user_id</c>,
/// <c>source_storage_path</c>, <c>source_sha256</c>, <c>claimed_at</c>. Un desalineamiento aquí no
/// rompe la compilación: revienta en runtime.</para>
/// </summary>
public sealed class StandaloneDocumentBatchEntity
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    /// <summary>Autor de la carga. Columna de negocio con FK, distinta de la de auditoría <see cref="CreatedBy"/>.</summary>
    public Guid CreatedByUserId { get; set; }

    public string Status { get; set; } = "queued";

    public string TemplateVersion { get; set; } = "v1";

    public string SourceFilename { get; set; } = string.Empty;

    /// <summary>Ruta opaca del XLSX. @pii:medium — no se expone en listados.</summary>
    public string SourceStoragePath { get; set; } = string.Empty;

    public string SourceSha256 { get; set; } = string.Empty;

    /// <summary>Filas de datos declaradas. CHECK <c>BETWEEN 0 AND 100</c> en base de datos.</summary>
    public int TotalItems { get; set; }

    public int GeneratedCount { get; set; }

    public int ErrorCount { get; set; }

    public string? IdempotencyKey { get; set; }

    /// <summary>Marca de toma por el worker. Base del reaper de lotes atascados (R5).</summary>
    public DateTimeOffset? ClaimedAt { get; set; }

    /// <summary>Fecha de cierre. El CHECK de cierre la exige en los tres estados terminales.</summary>
    public DateTimeOffset? CompletedAt { get; set; }

    public long RowVersion { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }

    public DateTimeOffset? DeletedAt { get; set; }

    public Guid? DeletedBy { get; set; }
}
