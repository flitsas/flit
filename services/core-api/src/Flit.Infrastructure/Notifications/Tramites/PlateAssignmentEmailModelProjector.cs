using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;

namespace Flit.Infrastructure.Notifications.Tramites;

/// <summary>
/// HU #11486 (ADR-0046) — proyecta el trámite persistido a <see cref="AsignacionPlacaEmailModel"/>.
/// OT desde field_values (misma fuente que FUR), nunca desde <see cref="ProcedureInstance.TransitOfficeId"/>.
/// </summary>
public static class PlateAssignmentEmailModelProjector
{
    public const string DefaultEstadoAsignado = "Asignado";

    public static AsignacionPlacaEmailModel Project(
        ProcedureInstance instance,
        IReadOnlyList<ProcedureInstanceActor> actors,
        IReadOnlyDictionary<string, string?> fieldValues)
    {
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentNullException.ThrowIfNull(actors);
        ArgumentNullException.ThrowIfNull(fieldValues);

        var comprador = FindActor(actors, "comprador");
        var ciudad = TramiteEmailCityResolver.Resolve(fieldValues, comprador);
        var secretaria = Get(fieldValues, "transit_office_name")?.Trim() ?? string.Empty;
        // ADR-0059 — este correo solo lo dispara la arista preasignacion → asignado (ADR-0046): el estado
        // que se comunica al cliente es, por definición, el de placa asignada.
        return new AsignacionPlacaEmailModel(
            ClienteNombre: comprador?.FullName?.Trim() ?? string.Empty,
            Placa: instance.Plate?.Trim() ?? string.Empty,
            EstadoActual: DefaultEstadoAsignado,
            Ciudad: ciudad,
            SecretariaTransito: secretaria);
    }

    private static ProcedureInstanceActor? FindActor(
        IReadOnlyList<ProcedureInstanceActor> actors, string role) =>
        actors.FirstOrDefault(a =>
            string.Equals(a.ActorType, role, StringComparison.OrdinalIgnoreCase));

    private static string? Get(IReadOnlyDictionary<string, string?> fieldValues, string key) =>
        fieldValues.TryGetValue(key, out var value) ? value : null;
}
