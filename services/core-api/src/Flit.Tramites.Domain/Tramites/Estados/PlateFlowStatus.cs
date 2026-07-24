namespace Flit.Tramites.Domain.Tramites.Estados;

/// <summary>
/// Sub-estado INTERNO del flujo de asignación de placa (Feature #10587, HU #10785). Es ortogonal al
/// <see cref="TramiteEstado"/> (status global): mientras el sub-flujo de placa avanza, el status del
/// trámite permanece en <c>entregado</c>. Se persiste en <c>procedure_instances.plate_flow_status</c>
/// (nullable): <c>null</c> = trámite sin ruta de placa (comportamiento estándar de develop).
/// </summary>
public static class PlateFlowStatus
{
    /// <summary>El trámite se entregó al OT y espera que le asignen una placa (Flujo B, sin rango).</summary>
    public const string Preasignado = "preasignado";

    /// <summary>El trámite ya tiene placa asignada al VIN (Flujo A directo, o Flujo B tras el OT). Pendiente de SOAT + recepción del OT.</summary>
    public const string Asignado = "asignado";

    /// <summary>Todos los sub-estados no nulos válidos (para validación de entrada y checks).</summary>
    public static readonly IReadOnlyList<string> Todos = [Preasignado, Asignado];

    /// <summary>¿<paramref name="value"/> es un sub-estado de placa conocido (no nulo)?</summary>
    public static bool EsValido(string? value) =>
        value is not null && Todos.Contains(value, StringComparer.Ordinal);
}
