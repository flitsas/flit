namespace Flit.Infrastructure.Persistence.Entities.Tramites;

/// <summary>
/// Tipo de documento del catálogo maestro — <c>tramites.document_types</c>
/// (DDL HU #10155, gestionado por la API en HU #10193). Catálogo global SuperAdmin
/// (sin RLS). Soft-delete vía <see cref="IsActive"/>.
///
/// HU #10520: se mapean <c>mime_types_allowed</c> y <c>max_size_bytes</c> para la
/// validación de carga por tipo (con respaldo a los límites globales). La columna
/// <c>external_refs</c> sigue sin mapearse (default de BD).
/// </summary>
public sealed class DocumentType
{
    public Guid Id { get; set; }

    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>
    /// HU #12065 — instrucción de cargue que lee el gestor en la tarjeta del paso Requisitos
    /// (<c>upload_instructions</c>). Distinta de <see cref="Description"/>, que es la nota interna
    /// del administrador. <c>null</c> ⇒ la tarjeta no muestra instrucción.
    /// </summary>
    public string? UploadInstructions { get; set; }

    /// <summary>MIME permitidos para este tipo (jsonb). Vacío ⇒ se aplican los defaults globales.</summary>
    public List<string> MimeTypesAllowed { get; set; } = [];

    /// <summary>Tamaño máximo en bytes para este tipo. <c>0</c> ⇒ se aplica el default global.</summary>
    public long MaxSizeBytes { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>
    /// HU #11181 — el documento lo produce FLIT (FUR, certificados, mandato, escrituras) en vez de
    /// adjuntarlo el gestor. Entra en la lista ordenable del OT y en el consolidado. No se pide
    /// ni se exige en el paso de Requisitos ni en los gates de radicación.
    /// </summary>
    public bool IsSystemGenerated { get; set; }

    /// <summary>
    /// HU #11181 — orden por defecto del documento generado en el expediente mientras el OT no
    /// configure su prelación. <c>null</c> en los documentos que solo se adjuntan.
    /// </summary>
    public short? GeneratedSortOrder { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }
}
