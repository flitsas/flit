using Flit.Admin.Application.Auditing;
using Flit.Admin.Application.Plataforma.Ai;
using Flit.Admin.Domain.Plataforma.Ai;
using Flit.Platform.Sdk.IaConsumo;
using Microsoft.Extensions.Logging;

namespace Flit.Infrastructure.Ai.Routing;

/// <summary>
/// Orquesta la cola de proveedores, failover en la misma request y persistencia best-effort
/// (HU #13510). Lee settings en cada invocación (sin caché — AC3).
/// </summary>
internal sealed class AiProviderRouter(
    IAiToolProviderSettingsRepository settingsRepository,
    IAiApiKeyEnvironmentProbe keyProbe,
    IEnumerable<IAiVisionProvider> visionProviders,
    IEnumerable<IAiChatProvider> chatProviders,
    IAdminAuditWriter auditWriter,
    ILogger<AiProviderRouter> logger,
    IIaCallContext? iaContexto = null,
    AiAutoReturnGate? autoReturnGate = null) : IAiProviderRouter
{
    /// <summary>Compuerta compartida del proceso cuando DI no inyecta una (pruebas antiguas, herramientas).</summary>
    private static readonly AiAutoReturnGate GateCompartida = new(AiAutoReturnGate.IntervaloPorDefecto);

    private readonly AiAutoReturnGate _gate = autoReturnGate ?? GateCompartida;

    /// <summary>Ámbito del registro de consumo de IA (ADR-0090): cada proveedor de la cola es una pasada.</summary>
    private readonly IIaCallContext _ia = iaContexto ?? IaCallContext.Instancia;

    private readonly Dictionary<string, IAiVisionProvider> _vision =
        visionProviders.ToDictionary(p => AiProviderCodes.Normalize(p.ProviderCode), StringComparer.Ordinal);

    private readonly Dictionary<string, IAiChatProvider> _chat =
        chatProviders.ToDictionary(p => AiProviderCodes.Normalize(p.ProviderCode), StringComparer.Ordinal);

    public Task<AiRouterResult> InvokeVisionAsync(
        string toolCode,
        string base64,
        string mediaType,
        string prompt,
        CancellationToken cancellationToken,
        string? model = null,
        int? maxTokens = null,
        int? timeoutSeconds = null,
        AiVisionPerfil perfil = AiVisionPerfil.Lectura) =>
        InvokeAsync(
            toolCode,
            async (providerCode, ct) =>
            {
                if (!_vision.TryGetValue(providerCode, out var provider))
                    return AiProviderCallResult.Fail(503, "provider_adapter_missing");

                return await provider
                    .SendVisionAsync(base64, mediaType, prompt, ct, model, maxTokens, timeoutSeconds, perfil)
                    .ConfigureAwait(false);
            },
            cancellationToken);

    public Task<AiRouterResult> InvokeChatAsync(
        string toolCode,
        IReadOnlyList<AiChatSystemBlock> system,
        IReadOnlyList<AiChatTurn> turns,
        CancellationToken cancellationToken,
        string? model = null,
        int? maxTokens = null,
        int? timeoutSeconds = null) =>
        InvokeAsync(
            toolCode,
            async (providerCode, ct) =>
            {
                if (!_chat.TryGetValue(providerCode, out var provider))
                    return AiProviderCallResult.Fail(503, "provider_adapter_missing");

                return await provider
                    .SendChatAsync(system, turns, ct, model, maxTokens, timeoutSeconds)
                    .ConfigureAwait(false);
            },
            cancellationToken);

    private async Task<AiRouterResult> InvokeAsync(
        string toolCode,
        Func<string, CancellationToken, Task<AiProviderCallResult>> invoke,
        CancellationToken cancellationToken)
    {
        var normalizedTool = AiToolCodes.Normalize(toolCode);
        var settings = await settingsRepository
            .GetByToolCodeAsync(normalizedTool, cancellationToken)
            .ConfigureAwait(false);

        var slots = settings?.Providers ?? AiProviderQueue.DefaultSlots;
        var active = settings?.ActiveProvider ?? AiProviderCodes.Anthropic;
        var queue = AiProviderQueue.Build(normalizedTool, active, slots, keyProbe.HasApiKey);

        if (queue.Count == 0)
        {
            AiRouterLog.EmptyQueue(logger, normalizedTool);
            return AiRouterResult.ProviderUnavailable();
        }

        // auto_return_to_primary: si un failover dejó la herramienta en un respaldo, cada cierto tiempo se
        // vuelve a intentar primero con el principal (prioridad 1 entre los candidatos). Si responde, vuelve a
        // ser el activo; si no, la petición sigue con el respaldo como siempre, sin contarlo como failover.
        var porPrioridad = AiProviderQueue.Build(normalizedTool, null, slots, keyProbe.HasApiKey);
        var principal = porPrioridad.Count > 0 ? porPrioridad[0] : null;
        var activoActual = queue[0];
        var probandoPrincipal = settings?.AutoReturnToPrimary == true
            && principal is not null
            && !string.Equals(principal, activoActual, StringComparison.Ordinal)
            && _gate.TryEnter(normalizedTool);
        if (probandoPrincipal)
        {
            AiRouterLog.ProbingPrimary(logger, normalizedTool, principal!, activoActual);
            queue = [principal!, .. queue.Where(p => !string.Equals(p, principal, StringComparison.Ordinal))];
        }

        string? failedFrom = null;
        AiProviderCallResult? lastFail = null;

        // Toda la cola es UNA operación de negocio: el failover a otro proveedor queda con el mismo
        // correlacion_id y pasada = posición del proveedor en la cola (1 = el activo).
        using var operacion = _ia.Iniciar(new IaAmbito());
        short pasada = 0;

        foreach (var providerCode in queue)
        {
            pasada++;
            AiProviderCallResult result;
            try
            {
                using (_ia.Iniciar(new IaAmbito { Pasada = pasada }))
                    result = await invoke(providerCode, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                AiRouterLog.AdapterThrew(logger, providerCode, ex.GetType().Name);
                result = AiProviderCallResult.Fail(503, "provider_exception");
            }

            var eraPruebaDelPrincipal = probandoPrincipal && pasada == 1;

            if (result.Ok && !string.IsNullOrWhiteSpace(result.Text))
            {
                if (eraPruebaDelPrincipal)
                {
                    await RecordAutoReturnBestEffortAsync(normalizedTool, activoActual, providerCode, cancellationToken)
                        .ConfigureAwait(false);
                }
                else if (failedFrom is not null)
                {
                    await RecordFailoverBestEffortAsync(
                            normalizedTool,
                            failedFrom,
                            providerCode,
                            lastFail?.Message,
                            cancellationToken)
                        .ConfigureAwait(false);
                }

                return AiRouterResult.Success(result.Text!, providerCode, result.Usage);
            }

            AiRouterLog.ProviderFailed(logger, normalizedTool, providerCode, result.Status);
            if (eraPruebaDelPrincipal)
            {
                // El principal sigue caído: no es un failover nuevo, el activo no cambia.
                AiRouterLog.PrimaryStillDown(logger, normalizedTool, providerCode);
                continue;
            }

            failedFrom ??= providerCode;
            lastFail = result;
        }

        AiRouterLog.AllFailed(logger, normalizedTool, queue.Count);
        return AiRouterResult.ProviderUnavailable();
    }

    private async Task RecordFailoverBestEffortAsync(
        string toolCode,
        string fromProvider,
        string toProvider,
        string? detail,
        CancellationToken cancellationToken)
    {
        // El principal no se vuelve a probar hasta un intervalo después de haberlo dejado.
        _gate.Marcar(toolCode);
        try
        {
            await settingsRepository
                .RecordFailoverAsync(toolCode, fromProvider, toProvider, detail, cancellationToken)
                .ConfigureAwait(false);

            await auditWriter
                .WriteAsync(
                    new AdminAuditEntry(
                        TenantId: null,
                        TenantType: null,
                        Module: AuditVocabulary.Modules.Config,
                        EntityName: "ai_tool_provider_settings",
                        Operation: AuditVocabulary.Operations.Failover,
                        Result: AuditVocabulary.Results.Success,
                        ErrorCode: null,
                        ActorUserId: null,
                        TargetEntityType: "AI_TOOL",
                        TargetEntityId: null,
                        ClientIp: null,
                        UserAgent: null,
                        OldValue: $"activeProvider={fromProvider}",
                        NewValue: $"activeProvider={toProvider};reason=failover_auto"),
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            AiRouterLog.FailoverPersistFailed(logger, toolCode, fromProvider, toProvider, ex.GetType().Name);
        }
    }

    private async Task RecordAutoReturnBestEffortAsync(
        string toolCode,
        string fromProvider,
        string toProvider,
        CancellationToken cancellationToken)
    {
        try
        {
            await settingsRepository
                .RecordAutoReturnAsync(toolCode, fromProvider, toProvider, cancellationToken)
                .ConfigureAwait(false);

            await auditWriter
                .WriteAsync(
                    new AdminAuditEntry(
                        TenantId: null,
                        TenantType: null,
                        Module: AuditVocabulary.Modules.Config,
                        EntityName: "ai_tool_provider_settings",
                        Operation: AuditVocabulary.Operations.AutoReturn,
                        Result: AuditVocabulary.Results.Success,
                        ErrorCode: null,
                        ActorUserId: null,
                        TargetEntityType: "AI_TOOL",
                        TargetEntityId: null,
                        ClientIp: null,
                        UserAgent: null,
                        OldValue: $"activeProvider={fromProvider}",
                        NewValue: $"activeProvider={toProvider};reason=auto_return"),
                    cancellationToken)
                .ConfigureAwait(false);

            AiRouterLog.AutoReturned(logger, toolCode, fromProvider, toProvider);
        }
        catch (Exception ex)
        {
            AiRouterLog.AutoReturnPersistFailed(logger, toolCode, fromProvider, toProvider, ex.GetType().Name);
        }
    }
}

internal static partial class AiRouterLog
{
    [LoggerMessage(Level = LogLevel.Information, Message = "AI router: probando de nuevo el principal {Primary} (tool {ToolCode}, activo {Active})")]
    public static partial void ProbingPrimary(ILogger logger, string toolCode, string primary, string active);

    [LoggerMessage(Level = LogLevel.Information, Message = "AI router: el principal {Primary} sigue sin responder (tool {ToolCode}); se sigue con el respaldo")]
    public static partial void PrimaryStillDown(ILogger logger, string toolCode, string primary);

    [LoggerMessage(Level = LogLevel.Information, Message = "AI router: regreso automático {FromProvider}→{ToProvider} (tool {ToolCode})")]
    public static partial void AutoReturned(ILogger logger, string toolCode, string fromProvider, string toProvider);

    [LoggerMessage(Level = LogLevel.Warning, Message = "AI router: no se pudo persistir el regreso {FromProvider}→{ToProvider} (tool {ToolCode}, {ExceptionType})")]
    public static partial void AutoReturnPersistFailed(
        ILogger logger,
        string toolCode,
        string fromProvider,
        string toProvider,
        string exceptionType);

    [LoggerMessage(Level = LogLevel.Warning, Message = "AI router: cola vacía para tool {ToolCode}")]
    public static partial void EmptyQueue(ILogger logger, string toolCode);

    [LoggerMessage(Level = LogLevel.Warning, Message = "AI router: proveedor {ProviderCode} falló (tool {ToolCode}, status {Status})")]
    public static partial void ProviderFailed(ILogger logger, string toolCode, string providerCode, int status);

    [LoggerMessage(Level = LogLevel.Warning, Message = "AI router: adapter {ProviderCode} lanzó {ExceptionType}")]
    public static partial void AdapterThrew(ILogger logger, string providerCode, string exceptionType);

    [LoggerMessage(Level = LogLevel.Warning, Message = "AI router: todos los candidatos fallaron (tool {ToolCode}, n={Count})")]
    public static partial void AllFailed(ILogger logger, string toolCode, int count);

    [LoggerMessage(Level = LogLevel.Warning, Message = "AI router: no se pudo persistir failover {FromProvider}→{ToProvider} (tool {ToolCode}, {ExceptionType})")]
    public static partial void FailoverPersistFailed(
        ILogger logger,
        string toolCode,
        string fromProvider,
        string toProvider,
        string exceptionType);
}
