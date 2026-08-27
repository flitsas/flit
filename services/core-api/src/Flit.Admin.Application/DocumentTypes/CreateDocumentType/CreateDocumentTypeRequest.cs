namespace Flit.Admin.Application.DocumentTypes.CreateDocumentType;

/// <summary>
/// Payload de alta de un tipo de documento (HU #10193, AC1):
/// <c>{ nombre, descripcion, obligatorio }</c>. El código lo genera el sistema.
/// </summary>
/// <param name="Codigo">Ignorado. El código se deriva del nombre.</param>
/// <param name="Nombre">Nombre visible (columna <c>name</c>).</param>
/// <param name="Descripcion">Descripción opcional (columna <c>description</c>).</param>
/// <param name="Obligatorio">
/// Aceptado por el AC pero <b>no persistido</b> en el catálogo: la obligatoriedad
/// vive en <c>tramites.procedure_document_requirements.is_mandatory</c> por trámite
/// (HU #10195), no en el tipo de documento. Se ignora intencionalmente aquí.
/// </param>
public sealed record CreateDocumentTypeRequest(
    string? Codigo,
    string? Nombre,
    string? Descripcion,
    bool? Obligatorio,
    // RF08/09 — límites por tipo. Opcionales: null/omitido ⇒ se aplican los globales por defecto.
    IReadOnlyList<string>? MimeTypesAllowed = null,
    long? MaxSizeBytes = null,
    // True = autogenerado (consolidado / sistema). False u omitido = cargue en Requisitos.
    bool? EsAutogenerado = null);
