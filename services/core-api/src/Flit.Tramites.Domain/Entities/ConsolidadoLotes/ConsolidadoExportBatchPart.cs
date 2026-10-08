namespace Flit.Tramites.Domain.Entities.ConsolidadoLotes;

/// <summary>
/// HU #13368 (Feature #13306, ADR-0070) — parte ZIP cifrada de un lote (<c>tramites.consolidado_export_batch_parts</c>).
/// Única por <c>(BatchId, PartNumber)</c>: esa pareja es la clave a la que apuntan los ítems. Las invariantes (binario
/// completo al cerrar, <c>purgada</c> ⇔ <see cref="PurgedAt"/>, lease al empaquetar) las garantiza la base (DDL 134).
/// </summary>
public sealed class ConsolidadoExportBatchPart
{
    public Guid Id { get; set; }

    /// <summary>
    /// Copia del tenant del lote. <b>La fija el trigger</b> <c>tr_consolidado_export_batch_parts_tenant</c>: el valor
    /// que envíe la aplicación se ignora. <c>null</c> en lotes de Super Admin (E5).
    /// </summary>
    public Guid? TenantId { get; set; }

    public Guid BatchId { get; set; }

    /// <summary>Número de la parte dentro del lote (desde 1).</summary>
    public short PartNumber { get; set; }

    /// <summary>Uno de <see cref="ConsolidadoExportPartStatus"/>.</summary>
    public string Status { get; set; } = ConsolidadoExportPartStatus.Pendiente;

    public short Attempts { get; set; }

    public DateTimeOffset? LeaseUntil { get; set; }

    public int PdfCount { get; set; }

    public int OmittedCount { get; set; }

    public long? PlainSizeBytes { get; set; }

    public long? StoredSizeBytes { get; set; }

    /// <summary>SHA-256 (hex en minúsculas) del texto cifrado almacenado.</summary>
    public string? StoredSha256 { get; set; }

    /// <summary>Id opaco del texto cifrado en el file-manager (no es una URL pública).</summary>
    public string? StoragePath { get; set; }

    public DateTimeOffset? ClosedAt { get; set; }

    public DateTimeOffset? PurgedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }

    /// <summary>Token de concurrencia; lo incrementa <c>tr_consolidado_export_batch_parts_row_version</c>.</summary>
    public long RowVersion { get; set; }
}
