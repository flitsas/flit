namespace Flit.Admin.Domain.OtDocumentPrecedence;

/// <summary>Entrada de prelación documental OT (HU #10222).</summary>
public sealed class OtDocumentPrecedenceItem
{
    public Guid Id { get; init; }

    public Guid TenantId { get; init; }

    public Guid ProcedureTypeId { get; init; }

    public Guid DocumentTypeId { get; init; }

    public string DocumentName { get; init; } = string.Empty;

    public short SortOrder { get; init; }
}
