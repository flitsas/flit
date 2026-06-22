namespace Flit.Admin.Application.DocumentTypes.CreateDocumentType;

/// <summary>
/// Payload de alta de un tipo de documento (HU #10193, AC1):
/// <c>{ codigo, nombre, descripcion, obligatorio }</c>.
/// </summary>
/// <param name="Codigo">Código único del catálogo (columna <c>code</c>).</param>
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
    bool? Obligatorio);
