using Flit.Admin.Domain.DocumentTypes;

namespace Flit.Admin.Application.DocumentTypes;

/// <summary>
/// Respuesta pública de un tipo de documento (HU #10193). Serializada en camelCase:
/// <c>{ id, codigo, nombre, descripcion, estado, fechaCreacion }</c>. <c>estado</c>
/// traduce <c>is_active</c> a <c>"activo"</c> / <c>"inactivo"</c> (mapeo del handoff).
/// </summary>
public sealed record DocumentTypeResponse(
    Guid Id,
    string Codigo,
    string Nombre,
    string? Descripcion,
    string Estado,
    DateTimeOffset FechaCreacion,
    IReadOnlyList<string> MimeTypesAllowed,
    long MaxSizeBytes)
{
    public const string EstadoActivo = "activo";
    public const string EstadoInactivo = "inactivo";

    public static DocumentTypeResponse From(DocumentTypeListItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        return new DocumentTypeResponse(
            item.Id,
            item.Code,
            item.Name,
            item.Description,
            item.IsActive ? EstadoActivo : EstadoInactivo,
            item.CreatedAt,
            item.MimeTypesAllowed,
            item.MaxSizeBytes);
    }
}
