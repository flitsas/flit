namespace Flit.Infrastructure.Persistence.Entities.Tramites;

/// <summary>
/// Tipo de documento del catálogo maestro — <c>tramites.document_types</c>
/// (DDL HU #10155, gestionado por la API en HU #10193). Catálogo global SuperAdmin
/// (sin RLS). Soft-delete vía <see cref="IsActive"/>.
///
/// Las columnas <c>mime_types_allowed</c>, <c>max_size_bytes</c> y <c>external_refs</c>
/// no se mapean: se gestionan por defaults de la BD y no las toca esta HU.
/// </summary>
public sealed class DocumentType
{
    public Guid Id { get; set; }

    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }
}
