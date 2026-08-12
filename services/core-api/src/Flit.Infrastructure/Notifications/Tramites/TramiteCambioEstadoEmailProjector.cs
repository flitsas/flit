using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;

namespace Flit.Infrastructure.Notifications.Tramites;

/// <summary>
/// HU #11463 — proyecta el trámite persistido a <see cref="TramiteCambioEstadoEmailModel"/>
/// sin tocar el composer. OT desde field_values (misma fuente que FUR), no desde TransitOfficeId.
/// </summary>
public static class TramiteCambioEstadoEmailProjector
{
    public static TramiteCambioEstadoEmailModel Project(
        ProcedureInstance instance,
        IReadOnlyList<ProcedureInstanceActor> actors,
        IReadOnlyDictionary<string, string?> fieldValues,
        string estadoActual)
    {
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentNullException.ThrowIfNull(actors);
        ArgumentNullException.ThrowIfNull(fieldValues);

        var esTraspaso = string.Equals(
            instance.ModalidadEntrada?.Trim(),
            "traspaso",
            StringComparison.OrdinalIgnoreCase);

        var comprador = FindActor(actors, "comprador");
        var vendedor = FindActor(actors, "vendedor");

        var ciudad = TramiteEmailCityResolver.Resolve(fieldValues, comprador);
        var ot = Get(fieldValues, "transit_office_name") ?? string.Empty;

        return new TramiteCambioEstadoEmailModel(
            VendedorNombre: esTraspaso ? (vendedor?.FullName?.Trim() ?? string.Empty) : string.Empty,
            CompradorNombre: comprador?.FullName?.Trim() ?? string.Empty,
            Placa: instance.Plate?.Trim() ?? string.Empty,
            CiudadOt: ciudad,
            NombreOt: ot.Trim(),
            EstadoActual: estadoActual,
            EsTraspaso: esTraspaso);
    }

    private static ProcedureInstanceActor? FindActor(
        IReadOnlyList<ProcedureInstanceActor> actors, string role) =>
        actors.FirstOrDefault(a =>
            string.Equals(a.ActorType, role, StringComparison.OrdinalIgnoreCase));

    private static string? Get(IReadOnlyDictionary<string, string?> fieldValues, string key) =>
        fieldValues.TryGetValue(key, out var value) ? value : null;
}
