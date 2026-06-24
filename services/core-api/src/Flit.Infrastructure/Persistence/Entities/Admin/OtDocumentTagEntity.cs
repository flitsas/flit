namespace Flit.Infrastructure.Persistence.Entities.Admin;

/// <summary>Etiqueta documental OT — <c>admin.ot_document_tags</c> (HU #10152 / #10222).</summary>
public sealed class OtDocumentTagEntity
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Color { get; set; } = "#162744";

    public DateTimeOffset CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }
}
