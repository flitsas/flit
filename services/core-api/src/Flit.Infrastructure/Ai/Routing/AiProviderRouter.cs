using Flit.Admin.Application.Auditing;
using Flit.Admin.Application.Plataforma.Ai;
using Flit.Admin.Domain.Plataforma.Ai;
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
    ILogger<AiProviderRouter> logger) : IAiProviderRouter
{
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
        int? timeoutSeconds = null) =>
        InvokeAsync(
            toolCode,
            async (providerCode, ct) =>
            {
                if (!_vision.TryGetValue(providerCode, out var provider))
                    return AiProviderCallResult.Fail(503, "provider_adapter_missing");

                return await provider
                    .SendVisionAsync(base64, mediaType, prompt, ct, model, maxTokens, timeoutSeconds)
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

        string? failedFrom = null;
        AiProviderCallResult? lastFail = null;

        foreach (var providerCode in queue)
        {
            AiProviderCallResult result;
            try
            {
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

            if (result.Ok && !string.IsNullOrWhiteSpace(result.Text))
            {
                if (failedFrom is not null)
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

            failedFrom ??= providerCode;
            lastFail = result;
            AiRouterLog.ProviderFailed(logger, normalizedTool, providerCode, result.Status);
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
}

internal static partial class AiRouterLog
{
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
