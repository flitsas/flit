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
    // Orden base del desglose (fallback si el tenant no fija un sugerido).
    private static readonly string[] BasePriority = ["fasecolda", "base_gravable", "mercado_libre"];

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
        var providers = registry.All();
        if (set is not null)
        {
            providers = providers
                .Where(p => set.Enabled.Contains(p.Key, StringComparer.OrdinalIgnoreCase))
                .ToList();
        }

        // Prioridad efectiva: el sugerido del tenant primero, luego el orden base.
        var priority = EffectivePriority(set?.Primary);

        // Ejecución en paralelo con aislamiento de fallos por fuente (AC#3).
        var results = await Task.WhenAll(providers.Select(p => RunSafeAsync(p, ctx, ct)));

        var (sugerido, fuente) = PickSuggested(results, priority);

        var ordered = results
            .OrderBy(r => IndexOf(r.Source, priority))
            .ToList();

        return (new SuggestedCommercialValue(sugerido, fuente, ordered), null);
    }

    /// <summary>Orden de prioridad con el sugerido del tenant al frente (sin duplicar).</summary>
    private static string[] EffectivePriority(string? primary)
    {
        if (string.IsNullOrWhiteSpace(primary))
            return BasePriority;

        var rest = BasePriority.Where(k => !string.Equals(k, primary, StringComparison.OrdinalIgnoreCase));
        return [primary, .. rest];
    }

    private static (long? Sugerido, string? Fuente) PickSuggested(
        IReadOnlyList<AvaluoResult> results,
        string[] priority)
    {
        foreach (var key in priority)
        {
            var hit = results.FirstOrDefault(r =>
                string.Equals(r.Source, key, StringComparison.OrdinalIgnoreCase) &&
                r.Status == "ok" && r.Value is not null);
            if (hit is not null)
                return (hit.Value, hit.Source);
        }

        var anyOk = results.FirstOrDefault(r => r.Status == "ok" && r.Value is not null);
        return anyOk is not null ? (anyOk.Value, anyOk.Source) : (null, null);
    }

    private static async Task<AvaluoResult> RunSafeAsync(IAvaluoProvider provider, AvaluoContext ctx, CancellationToken ct)
    {
        try
        {
            return await provider.GetAvaluoAsync(ctx, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
#pragma warning disable CA1031 // Resiliencia (AC#3): una fuente que falle no debe tumbar la sugerencia.
        catch (Exception)
        {
            return AvaluoResult.Error(provider.Key);
        }
#pragma warning restore CA1031
    }

    private static int IndexOf(string source, string[] priority)
    {
        var i = Array.IndexOf(priority, source);
        return i >= 0 ? i : int.MaxValue;
    }
}
