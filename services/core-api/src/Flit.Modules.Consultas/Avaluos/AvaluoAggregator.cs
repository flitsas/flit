namespace Flit.Tramites.Application.UseCases.Avaluos;

/// <summary>
/// Agrega en PARALELO las fuentes de avalúo habilitadas (Feature #10707, ADR-0029) y elige el valor sugerido. Tolera
/// fallo parcial: una fuente que falla o no tiene datos queda marcada y las demás siguen; nunca lanza (salvo
/// cancelación). Salió del handler de Trámites (HU #13343) para que core-consultas aplique la MISMA regla.
/// </summary>
public static class AvaluoAggregator
{
    // Orden base del desglose (fallback si el tenant no fija un sugerido).
    private static readonly string[] BasePriority = ["fasecolda", "base_gravable", "mercado_libre"];

    /// <param name="set">Proveedores habilitados y sugerido del tenant; null ⇒ se corren todos los registrados.</param>
    public static async Task<SuggestedCommercialValue> SuggestAsync(
        IAvaluoProviderRegistry registry, AvaluoEnabledSet? set, AvaluoContext ctx, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(ctx);

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
        var results = await Task.WhenAll(providers.Select(p => RunSafeAsync(p, ctx, ct))).ConfigureAwait(false);

        var (sugerido, fuente) = PickSuggested(results, priority);
        var ordered = results
            .OrderBy(r => IndexOf(r.Source, priority))
            .ToList();

        return new SuggestedCommercialValue(sugerido, fuente, ordered);
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
            return await provider.GetAvaluoAsync(ctx, ct).ConfigureAwait(false);
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
