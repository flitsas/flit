namespace Flit.Infrastructure.Persistence.Entities.Admin;

/// <summary>Prelación documental OT — <c>admin.ot_document_precedence</c> (HU #10152 / #10222).</summary>
public sealed class OtDocumentPrecedenceEntity
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    public Guid ProcedureTypeId { get; set; }

    public Guid DocumentTypeId { get; set; }

    public short SortOrder { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }
}
