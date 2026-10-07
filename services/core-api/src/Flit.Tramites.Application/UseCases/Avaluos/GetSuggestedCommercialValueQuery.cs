using Flit.Tramites.Domain.Repositories;

namespace Flit.Tramites.Application.UseCases.Avaluos;

/// <summary>
/// Sugiere el valor comercial del vehículo agregando en PARALELO todas las fuentes de avalúo
/// registradas (Feature #10707, ADR-0029). Tolera fallo parcial: si una fuente falla o no tiene
/// datos, la respuesta incluye las demás y marca su estado; nunca lanza. El valor sugerido toma
/// la primera fuente disponible según prioridad (Fasecolda principal).
/// </summary>
public sealed class GetSuggestedCommercialValueHandler(
    IProcedureInstanceRepository instanceRepo,
    IAvaluoProviderRegistry registry,
    IAvaluoProviderPolicy? policy = null)
{
    public async Task<(SuggestedCommercialValue? Result, string? Error)> HandleAsync(
        Guid instanceId,
        Guid tenantId,
        CancellationToken ct = default)
    {
        var instance = await instanceRepo.GetByIdWithDetailsAsync(instanceId, tenantId, ct);
        if (instance is null)
            return (null, "instance_not_found");

        var fieldValues = instance.FieldValues
            .ToDictionary(f => f.FieldKey, f => f.ValueText, StringComparer.OrdinalIgnoreCase);
        var ctx = new AvaluoContext(instance.Id, instance.TenantId, fieldValues);

        // Proveedores habilitados + sugerido según la config del tenant (Feature #10707). Sin política
        // registrada (p. ej. tests que construyen el handler directo) ⇒ se corren todos los registrados.
        var set = policy is not null ? await policy.GetAsync(tenantId, ct) : null;

        // La agregación vive en el módulo de consultas (HU #13343): la misma regla para core-consultas.
        return (await AvaluoAggregator.SuggestAsync(registry, set, ctx, ct), null);
    }
}
