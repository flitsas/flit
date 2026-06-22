namespace Flit.Admin.Domain.DocumentTypes;

/// <summary>
/// Read model de un tipo de documento del catálogo maestro (HU #10193).
/// Proyección sobre <c>tramites.document_types</c>. El estado activo/inactivo
/// (soft-delete) se expone como <see cref="IsActive"/>; la capa de aplicación lo
/// traduce a <c>"activo"</c> / <c>"inactivo"</c> en la respuesta.
/// </summary>
public sealed class DocumentTypeListItem
{
    public Guid Id { get; init; }

    /// <summary>Código — <c>tramites.document_types.code</c>.</summary>
    public string Code { get; init; } = string.Empty;

    /// <summary>Nombre — <c>tramites.document_types.name</c>.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Descripción — <c>tramites.document_types.description</c>. Opcional.</summary>
    public string? Description { get; init; }

    /// <summary>Estado activo — <c>tramites.document_types.is_active</c> (false = inactivo).</summary>
    public bool IsActive { get; init; }

    /// <summary>Fecha de creación — <c>tramites.document_types.created_at</c>.</summary>
    public DateTimeOffset CreatedAt { get; init; }
}
