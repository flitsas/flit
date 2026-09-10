using Flit.Admin.Domain.DocumentTypes;

namespace Flit.Admin.Application.DocumentTypes;

/// <summary>
/// Respuesta pública de un tipo de documento (HU #10193). Serializada en camelCase:
/// <c>{ id, codigo, nombre, descripcion, estado, fechaCreacion }</c>. <c>estado</c>
/// traduce <c>is_active</c> a <c>"activo"</c> / <c>"inactivo"</c> (mapeo del handoff).
/// <c>esAutogenerado</c> mapea <c>is_system_generated</c>. <c>instruccionCargue</c> mapea
/// <c>upload_instructions</c>: el texto que lee el gestor al cargar (HU #12065), distinto de
/// <c>descripcion</c>, que es la nota interna del administrador.
/// </summary>
public sealed record DocumentTypeResponse(
    Guid Id,
    string Codigo,
    string Nombre,
    string? Descripcion,
    string Estado,
    DateTimeOffset FechaCreacion,
    IReadOnlyList<string> MimeTypesAllowed,
    long MaxSizeBytes,
    bool EsAutogenerado,
    string? InstruccionCargue)
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
            item.MaxSizeBytes,
            item.IsSystemGenerated,
            item.UploadInstructions);
    }
}
