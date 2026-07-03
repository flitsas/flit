namespace Flit.Tramites.Application.UseCases.Consultations;

/// <summary>
/// Implementación de <see cref="IConsultationProviderChainResolver"/> (HU #10478). Recorre la cadena
/// configurada, resuelve cada provider vía <see cref="IConsultationProviderRegistry"/> y cae al
/// siguiente cuando el resultado trae un check <c>error</c> (no verificable) o el primario supera el
/// presupuesto de failover. Los provider keys no registrados se saltan (p. ej. un deploy sin Verifik).
/// Recibe <see cref="ConsultationChainOptions"/> ya resuelto (el DI le pasa <c>IOptions.Value</c>) para
/// no acoplar la capa de aplicación al paquete de Options.
/// </summary>
public sealed class ConsultationProviderChainResolver(
    IConsultationProviderRegistry registry,
    ConsultationChainOptions options) : IConsultationProviderChainResolver
{
    private const string ErrorStatus = "error";

    private readonly ConsultationChainOptions _options = options;

    public IReadOnlyList<string> ResolveChain(ConsultationKind kind) =>
        _options.ChainFor(kind.ToConfigKey());

    public async Task<ConsultationResult> ConsultAsync(
        ConsultationKind kind, ConsultationContext ctx, int? failoverTimeoutMs, CancellationToken ct)
    {
        var chain = ResolveChain(kind);
        var timeout = failoverTimeoutMs ?? _options.FailoverTimeoutMs;

        ConsultationResult? last = null;

        for (var i = 0; i < chain.Count; i++)
        {
            var provider = registry.Resolve(chain[i]);
            if (provider is null)
                continue;

            // Solo se acota con timeout mientras quede un proveedor de fallback por delante; el último
            // corre con su propio presupuesto (HttpClient.Timeout) para no cortar la única respuesta.
            var applyTimeout = i < chain.Count - 1;
            var result = await RunAsync(provider, ctx, applyTimeout ? timeout : null, ct);

            if (!HasError(result))
                return result;

            last = result;
        }

        return last ?? NoProviderAvailable(kind);
    }

    private static async Task<ConsultationResult> RunAsync(
        IConsultationProvider provider, ConsultationContext ctx, int? timeoutMs, CancellationToken ct)
    {
        if (timeoutMs is not > 0)
            return await provider.ConsultAsync(ctx, ct);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(timeoutMs.Value);

        try
        {
            return await provider.ConsultAsync(ctx, timeoutCts.Token);
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            // El primario excedió el presupuesto de failover (no fue cancelación del caller): se trata
            // como no verificable para forzar el fallback al siguiente proveedor de la cadena.
            return TimedOut(provider.Key);
        }
    }

    private static bool HasError(ConsultationResult result) =>
        result.Checks.Any(c => string.Equals(c.Status, ErrorStatus, StringComparison.OrdinalIgnoreCase));

    private static ConsultationResult TimedOut(string providerKey) =>
        new(providerKey, "red",
            [new ConsultationCheck("provider", "Consulta RUNT", ErrorStatus, providerKey,
                "El proveedor de consulta no respondió a tiempo.")],
            []);

    private static ConsultationResult NoProviderAvailable(ConsultationKind kind) =>
        new("chain", "red",
            [new ConsultationCheck("provider", "Consulta RUNT", ErrorStatus, "chain",
                $"No hay proveedor de consulta disponible para {kind.ToConfigKey()}.")],
            []);
}
