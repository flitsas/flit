using Flit.Tramites.Domain.Documents;

namespace Flit.Tramites.Application.Documents;

/// <summary>
/// Cómo se pinta el recuadro MANDATARIO. Si hay estampa (baúl o sello de identidad), se usa:
/// el modelo «a mano» no oculta una firma que el mandatario ya tiene.
/// </summary>
public static class MandatoFirmaModoResolver
{
    public static MandatarioFirmaModo Resolve(
        string? assignmentMode,
        bool tieneConvenio,
        bool tieneEstampa)
    {
        if (MandatoAssignmentModeCodes.IsInstitutional(assignmentMode) || tieneConvenio)
            return MandatarioFirmaModo.SinBloque;

        if (MandatoAssignmentModeCodes.IsOpen(assignmentMode) || !tieneEstampa)
            return MandatarioFirmaModo.Manual;

        return MandatarioFirmaModo.Estampada;
    }

    public static bool TieneEstampa(MandatarioFirmante? mandatario) =>
        mandatario?.FirmaImagen is { Length: > 0 }
        || !string.IsNullOrWhiteSpace(mandatario?.SelloIdentidad);
}
