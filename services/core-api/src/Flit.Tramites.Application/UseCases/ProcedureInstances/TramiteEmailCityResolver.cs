using Flit.Tramites.Domain.Entities;

namespace Flit.Tramites.Application.UseCases.ProcedureInstances;

/// <summary>
/// Resuelve la ciudad legible para plantillas de correo de trámite: OT (field_values) con
/// preferencia sobre metadata del comprador (HU #11489 H4).
/// </summary>
public static class TramiteEmailCityResolver
{
    /// <summary>
    /// OT city legible → metadata Ciudad del comprador → vacío (omitir en plantilla).
    /// No usa <c>Direccion</c>.
    /// </summary>
    public static string Resolve(
        IReadOnlyDictionary<string, string?> fieldValues,
        ProcedureInstanceActor? compradorActor)
    {
        ArgumentNullException.ThrowIfNull(fieldValues);

        var otCity = TransitOfficeCity.Legible(Get(fieldValues, "transit_office_city"));
        if (!string.IsNullOrEmpty(otCity))
            return otCity;

        var metadataCiudad = ActorMetadataReader.GetCiudad(compradorActor?.Metadata);
        return string.IsNullOrWhiteSpace(metadataCiudad) ? string.Empty : metadataCiudad.Trim();
    }

    private static string? Get(IReadOnlyDictionary<string, string?> fieldValues, string key) =>
        fieldValues.TryGetValue(key, out var value) ? value : null;
}
