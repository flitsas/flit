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
    IAvaluoProviderRegistry registry)
{
    // Prioridad del valor sugerido y del orden del desglose.
    private static readonly string[] Priority = ["fasecolda", "base_gravable", "mercado_libre"];

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

        // Ejecución en paralelo con aislamiento de fallos por fuente (AC#3).
        var results = await Task.WhenAll(registry.All().Select(p => RunSafeAsync(p, ctx, ct)));

        var (sugerido, fuente) = PickSuggested(results);

        var ordered = results
            .OrderBy(r => IndexOf(r.Source))
            .ToList();

        return (new SuggestedCommercialValue(sugerido, fuente, ordered), null);
    }

    private static (long? Sugerido, string? Fuente) PickSuggested(IReadOnlyList<AvaluoResult> results)
    {
        foreach (var key in Priority)
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

    private static int IndexOf(string source)
    {
        var i = Array.IndexOf(Priority, source);
        return i >= 0 ? i : int.MaxValue;
    }
}
