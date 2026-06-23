namespace Flit.Admin.Domain.OtDocumentTags;

/// <summary>Etiqueta de documento OT (HU #10222).</summary>
public sealed class OtDocumentTag
{
    public Guid Id { get; init; }

    public Guid TenantId { get; init; }

    public string Code { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public string Color { get; init; } = "#162744";
}
