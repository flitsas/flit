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

    /// <summary>
    /// Instrucción de cargue (HU #12065) — <c>tramites.document_types.upload_instructions</c>.
    /// Es el texto que el gestor lee en la tarjeta del paso Requisitos, no la nota interna del
    /// administrador (<see cref="Description"/>). Opcional.
    /// </summary>
    public string? UploadInstructions { get; init; }

    /// <summary>Estado activo — <c>tramites.document_types.is_active</c> (false = inactivo).</summary>
    public bool IsActive { get; init; }

    /// <summary>Fecha de creación — <c>tramites.document_types.created_at</c>.</summary>
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>
    /// Formatos MIME permitidos por tipo (RF08) — <c>tramites.document_types.mime_types_allowed</c>.
    /// Lista vacía ⇒ se aplican los formatos globales por defecto al cargar.
    /// </summary>
    public IReadOnlyList<string> MimeTypesAllowed { get; init; } = [];

    /// <summary>
    /// Tamaño máximo por tipo en bytes (RF09) — <c>tramites.document_types.max_size_bytes</c>.
    /// <c>0</c> ⇒ se aplica el tamaño máximo global por defecto al cargar.
    /// </summary>
    public long MaxSizeBytes { get; init; }

    /// <summary>
    /// <c>tramites.document_types.is_system_generated</c>: true = autogenerado (consolidado,
    /// sin slot de carga ni gate de radicación); false = cargue en Requisitos.
    /// </summary>
    public bool IsSystemGenerated { get; init; }
}
