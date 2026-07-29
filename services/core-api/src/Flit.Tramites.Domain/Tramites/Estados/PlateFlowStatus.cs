namespace Flit.Tramites.Domain.Tramites.Estados;

/// <summary>
/// Sub-estado INTERNO del flujo de asignación de placa (Feature #10587 + extensión post-radicación).
/// Ortogonal al <see cref="TramiteEstado"/>: mientras avanza, el status permanece en <c>entregado</c>.
/// Se persiste en <c>procedure_instances.plate_flow_status</c> (nullable): <c>null</c> = sin ruta de placa.
/// </summary>
public static class PlateFlowStatus
{
    /// <summary>Sin placa (dígito opcional): espera asignación del OT. UI: "Sin asignar".</summary>
    public const string Preasignado = "preasignado";

    /// <summary>Ya hay placa: el gestor puede marcar checks opcionales y procesar. UI: "Asignado".</summary>
    public const string Asignado = "asignado";

    /// <summary>Listo para que el OT apruebe/rechace. UI: "Terminado".</summary>
    public const string Terminado = "terminado";

    /// <summary>Todos los sub-estados no nulos válidos.</summary>
    public static readonly IReadOnlyList<string> Todos = [Preasignado, Asignado, Terminado];

    /// <summary>¿<paramref name="value"/> es un sub-estado de placa conocido (no nulo)?</summary>
    public static bool EsValido(string? value) =>
        value is not null && Todos.Contains(value, StringComparer.Ordinal);

    /// <summary>¿El OT puede aprobar/rechazar con este sub-estado? Ruta estándar (<c>null</c>) o Terminado.</summary>
    public static bool PermiteDecisionOt(string? value) =>
        value is null || value == Terminado;
}
