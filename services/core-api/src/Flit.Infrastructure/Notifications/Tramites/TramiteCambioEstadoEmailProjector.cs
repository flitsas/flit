using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Enums;
using Flit.Tramites.Domain.Tramites.Services;

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
        string estadoActual,
        IReadOnlyList<string>? causalesRechazo = null,
        string? observacionRechazo = null,
        string? nombreTipoTramite = null)
    {
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentNullException.ThrowIfNull(actors);
        ArgumentNullException.ThrowIfNull(fieldValues);

        // ADR-0051 — «¿hay parte vendedora que nombrar en el correo?» lo declara el TIPO
        // (`requiresSeller`), no su familia. La familia solo aproximaba la respuesta: acierta en todos
        // los tipos sembrados hoy y seguiría acertando en `TRASPASO_UNILATERAL` por casualidad, pero
        // el día que un tipo de otra familia declare parte vendedora el correo saldría sin nombrarla.
        // El nombre del campo del modelo (`EsTraspaso`) es contrato con la plantilla y no cambia.
        var profile = ProcedureTypeGateProfile.FromJson(instance.ProcedureType?.GateProfile);
        var esTraspaso = profile.RequiresSeller;

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
            EsTraspaso: esTraspaso,
            CausalesRechazo: causalesRechazo,
            ObservacionRechazo: observacionRechazo,
            NombreTipoTramite: nombreTipoTramite?.Trim() ?? string.Empty);
    }

    private static ProcedureInstanceActor? FindActor(
        IReadOnlyList<ProcedureInstanceActor> actors, string role) =>
        actors.FirstOrDefault(a =>
            string.Equals(a.ActorType, role, StringComparison.OrdinalIgnoreCase));

    private static string? Get(IReadOnlyDictionary<string, string?> fieldValues, string key) =>
        fieldValues.TryGetValue(key, out var value) ? value : null;
}
