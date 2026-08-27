namespace Flit.Admin.Application.DocumentTypes.UpdateDocumentType;

/// <summary>
/// Payload de actualización de un tipo de documento (HU #10193, AC3):
/// <c>{ codigo, nombre, descripcion }</c>.
/// </summary>
public sealed record UpdateDocumentTypeRequest(
    string? Codigo,
    string? Nombre,
    string? Descripcion,
    // RF08/09 — límites por tipo. Opcionales: null/omitido ⇒ no se modifican (conserva lo existente).
    IReadOnlyList<string>? MimeTypesAllowed = null,
    long? MaxSizeBytes = null,
    // Null ⇒ conserva el origen. True = autogenerado; false = cargue.
    bool? EsAutogenerado = null);
