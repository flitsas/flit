namespace Flit.Tramites.Application.UseCases.Avaluos;

/// <summary>
/// HU #13348 (Epic #13316) — valor comercial sugerido de un vehículo a partir de sus datos. core-api lo pide a
/// core-consultas (no tiene proveedores); core-consultas lo resuelve en proceso con <see cref="AvaluoSugeridorEnProceso"/>.
/// </summary>
public interface IAvaluoSugeridor
{
    /// <param name="fieldValues">Datos del vehículo (vin, plate, vehicle_year, vehicle_engine_displacement, vehicle_fuel, vehicle_passengers).</param>
    Task<SuggestedCommercialValue> SugerirAsync(Guid instanceId, Guid tenantId, IReadOnlyDictionary<string, string?> fieldValues, CancellationToken ct);
}

/// <summary>Las fuentes habilitadas de la empresa en paralelo (Feature #10707): la regla de <see cref="AvaluoAggregator"/>.</summary>
public sealed class AvaluoSugeridorEnProceso(IAvaluoProviderRegistry registry, IAvaluoProviderPolicy? policy = null) : IAvaluoSugeridor
{
    public async Task<SuggestedCommercialValue> SugerirAsync(Guid instanceId, Guid tenantId, IReadOnlyDictionary<string, string?> fieldValues, CancellationToken ct)
    {
        // Sin política (p. ej. pruebas) se corren todas las fuentes registradas.
        var set = policy is not null ? await policy.GetAsync(tenantId, ct).ConfigureAwait(false) : null;
        return await AvaluoAggregator.SuggestAsync(registry, set, new AvaluoContext(instanceId, tenantId, fieldValues), ct).ConfigureAwait(false);
    }
}
