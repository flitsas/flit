namespace Flit.Tramites.Domain.Tramites.Services;

/// <summary>
/// HU #10872 (AC1) — mapeo EXPLÍCITO campo→gate para la re-radicación selectiva desde
/// <c>subsanacion</c>: dado el conjunto de <c>field_key</c> corregidos (el DIFF de
/// <see cref="FieldValueSnapshot"/>), resuelve qué categorías de gate de negocio deben
/// re-evaluarse. El gate de entrega al OT (tipo publicado + organismo habilitado/operable/reglas,
/// <c>TramiteLifecycleService.EvaluarEntregaAsync</c>) es el GATE FINAL de radicación: siempre corre,
/// fuera de este mapeo.
/// </summary>
public static class SubsanacionGateMap
{
    /// <summary>Precondición registral CF-03 (vehículo ya matriculado) — depende del VIN.</summary>
    public const string VehicleState = "vehicle_state";

    /// <summary>
    /// Gate de preparación RF03/R10 (identidad aprobada+vigente, documentos obligatorios, impronta y
    /// prenda del traspaso) — todo lo que no sea el VIN puede afectar documentos/organismo/prenda, así
    /// que es el bucket por defecto para cualquier otro campo corregido.
    /// </summary>
    public const string PreparationGate = "preparation_gate";

    /// <summary>Todas las categorías conocidas.</summary>
    public static IReadOnlySet<string> AllGates { get; } =
        new HashSet<string>(StringComparer.Ordinal) { VehicleState, PreparationGate };

    /// <summary>
    /// Fail-safe cuando NO hay snapshot base (trámite que entró a <c>subsanacion</c> antes de HU #10872,
    /// o metadata degradada): preserva EXACTAMENTE el comportamiento previo a esta HU para la
    /// re-radicación (<c>VehicleState</c> corría siempre para cualquier transición a <c>entregado</c>;
    /// <c>PreparationGate</c> NUNCA corría para <c>subsanacion→entregado</c>). Evita que la ausencia de
    /// snapshot (dato legado) bloquee re-radicaciones que antes pasaban.
    /// </summary>
    public static IReadOnlySet<string> NoBaselineFallback { get; } =
        new HashSet<string>(StringComparer.Ordinal) { VehicleState };

    /// <summary>
    /// Resuelve las categorías de gate afectadas por el conjunto de <paramref name="changedFieldKeys"/>.
    /// Conjunto vacío = ningún campo cambió → ninguna categoría se re-evalúa (solo corre el gate final).
    /// </summary>
    public static IReadOnlySet<string> ResolveGates(IReadOnlySet<string> changedFieldKeys)
    {
        ArgumentNullException.ThrowIfNull(changedFieldKeys);

        var gates = new HashSet<string>(StringComparer.Ordinal);
        foreach (var key in changedFieldKeys)
        {
            gates.Add(string.Equals(key, "vin", StringComparison.OrdinalIgnoreCase)
                ? VehicleState
                : PreparationGate);
        }

        return gates;
    }
}
