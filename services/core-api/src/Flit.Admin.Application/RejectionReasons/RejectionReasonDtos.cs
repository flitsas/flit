using Flit.Admin.Domain.RejectionReasons;

namespace Flit.Admin.Application.RejectionReasons;

/// <summary>Causal de rechazo tal como la devuelve la API.</summary>
public sealed record RejectionReasonResponse(
    Guid Id,
    string Code,
    string Description,
    string Modalidad,
    int SortOrder,
    bool IsActive);

/// <summary>Alta de causal. <c>SortOrder</c> nulo la manda al final de su modalidad.</summary>
public sealed record CreateRejectionReasonRequest(
    string? Code,
    string? Description,
    string? Modalidad,
    int? SortOrder);

public sealed record UpdateRejectionReasonRequest(
    string? Code,
    string? Description,
    string? Modalidad,
    int? SortOrder);

/// <summary>Activación/desactivación. No hay borrado (ver <see cref="IRejectionReasonRepository"/>).</summary>
public sealed record SetRejectionReasonActiveRequest(bool IsActive);

internal static class RejectionReasonMapper
{
    public static RejectionReasonResponse ToResponse(RejectionReasonItem item) =>
        new(item.Id, item.Code, item.Description, item.Family, item.SortOrder, item.IsActive);
}

/// <summary>
/// Validación compartida por alta y edición. El código se normaliza a slug en minúsculas porque es
/// la llave estable de los reportes: aceptar «SOAT No Vigente» y «soat_no_vigente» como códigos
/// distintos reintroduciría por la puerta de atrás el problema del texto libre.
/// </summary>
internal static class RejectionReasonValidator
{
    public const int CodeMaxLength = 60;
    public const int DescriptionMaxLength = 150;

    public static string? Validate(string? code, string? description, string? modalidad)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return "El código de la causal es obligatorio.";
        }

        if (code.Trim().Length > CodeMaxLength)
        {
            return $"El código no puede superar {CodeMaxLength} caracteres.";
        }

        if (string.IsNullOrWhiteSpace(description))
        {
            return "La descripción de la causal es obligatoria.";
        }

        if (description.Trim().Length > DescriptionMaxLength)
        {
            return $"La descripción no puede superar {DescriptionMaxLength} caracteres.";
        }

        return RejectionReasonFamilies.EsValida(modalidad)
            ? null
            : "La modalidad debe ser 'matricula_inicial' o 'traspaso'.";
    }

    public static string NormalizeCode(string code) =>
        code.Trim().ToLowerInvariant().Replace(' ', '_');
}
