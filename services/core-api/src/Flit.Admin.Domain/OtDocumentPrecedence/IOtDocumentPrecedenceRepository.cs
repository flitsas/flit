namespace Flit.Admin.Domain.OtDocumentPrecedence;

/// <summary>Repositorio de prelación documental OT — <c>admin.ot_document_precedence</c> (HU #10222).</summary>
public interface IOtDocumentPrecedenceRepository
{
    /// <summary>
    /// Lista completa de documentos ordenables del trámite (HU #11182): matriz base ∪ documentos
    /// generados ∪ overrides del OT, deduplicada por documento. Devuelve resultados aunque el OT
    /// nunca haya configurado nada (antes salía vacía y la pantalla era inoperante).
    /// </summary>
    Task<IReadOnlyList<OtDocumentPrecedenceItem>> ListByProcedureTypeAsync(
        Guid tenantId,
        Guid procedureTypeId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Guarda el orden como <b>upsert</b> (HU #11182): crea las filas que falten y actualiza las
    /// existentes. Devuelve <c>null</c> solo si el lote es inválido (vacío, demasiado grande o con
    /// documentos que no existen en el catálogo).
    /// </summary>
    Task<IReadOnlyList<OtDocumentPrecedenceItem>?> ReorderBatchAsync(
        Guid tenantId,
        Guid procedureTypeId,
        IReadOnlyList<OtDocumentPrecedenceOrderItem> items,
        Guid? changedBy,
        CancellationToken cancellationToken = default);
}

public sealed class OtDocumentPrecedenceOrderItem
{
    public Guid DocumentTypeId { get; init; }

    public short SortOrder { get; init; }
}
