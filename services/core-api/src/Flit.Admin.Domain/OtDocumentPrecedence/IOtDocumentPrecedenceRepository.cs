namespace Flit.Admin.Domain.OtDocumentPrecedence;

/// <summary>Repositorio de prelación documental OT — <c>admin.ot_document_precedence</c> (HU #10222).</summary>
public interface IOtDocumentPrecedenceRepository
{
    Task<IReadOnlyList<OtDocumentPrecedenceItem>> ListByProcedureTypeAsync(
        Guid tenantId,
        Guid procedureTypeId,
        CancellationToken cancellationToken = default);

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
