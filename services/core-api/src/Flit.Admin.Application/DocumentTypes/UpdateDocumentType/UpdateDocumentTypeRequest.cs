namespace Flit.Admin.Application.DocumentTypes.UpdateDocumentType;

/// <summary>
/// Payload de actualización de un tipo de documento (HU #10193, AC3):
/// <c>{ codigo, nombre, descripcion }</c>.
/// </summary>
public sealed record UpdateDocumentTypeRequest(
    string? Codigo,
    string? Nombre,
    string? Descripcion);
