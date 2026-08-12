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
        var ciudad = TransitOfficeCity.Legible(Get(fieldValues, "transit_office_city")) ?? string.Empty;
        var secretaria = Get(fieldValues, "transit_office_name")?.Trim() ?? string.Empty;
        var estado = NormalizeEstado(instance.PlateFlowStatus);

        return new AsignacionPlacaEmailModel(
            ClienteNombre: comprador?.FullName?.Trim() ?? string.Empty,
            Placa: instance.Plate?.Trim() ?? string.Empty,
            EstadoActual: estado,
            Ciudad: ciudad,
            SecretariaTransito: secretaria);
    }

    private static string NormalizeEstado(string? plateFlowStatus)
    {
        var value = plateFlowStatus?.Trim();
        return string.IsNullOrEmpty(value) ? DefaultEstadoAsignado : value;
    }

    private static ProcedureInstanceActor? FindActor(
        IReadOnlyList<ProcedureInstanceActor> actors, string role) =>
        actors.FirstOrDefault(a =>
            string.Equals(a.ActorType, role, StringComparison.OrdinalIgnoreCase));

    private static string? Get(IReadOnlyDictionary<string, string?> fieldValues, string key) =>
        fieldValues.TryGetValue(key, out var value) ? value : null;
}
