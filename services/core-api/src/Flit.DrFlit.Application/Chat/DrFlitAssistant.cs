using System.Diagnostics;
using Flit.DrFlit.Application.Abstractions;
using Flit.DrFlit.Application.Manual;
using Microsoft.Extensions.Logging;

namespace Flit.DrFlit.Application.Chat;

/// <summary>
/// Implementación de <see cref="IDrFlitAssistant"/> (HU #12919, ADR-0060 §6–§7.1). Orden:
/// <list type="number">
///   <item>LLM apagado o sin manual ⇒ degradado, sin consumir tope (no hubo costo).</item>
///   <item>Consume un mensaje del tope diario; si ya estaba alcanzado ⇒ <c>rate_limited</c> sin llamar al LLM.</item>
///   <item>Llama al modelo y valida la salida; cualquier desviación ⇒ degradado.</item>
/// </list>
/// Nunca loguea el texto del usuario ni el del modelo (§10): solo estado, intención, tokens y latencia.
/// </summary>
public sealed class DrFlitAssistant(
    IDrFlitChatModel model,
    IDrFlitUsageCounter counter,
    IDrFlitManualCatalogProvider catalogProvider,
    IDrFlitChatSettings settings,
    ILogger<DrFlitAssistant> logger) : IDrFlitAssistant
{
    /// <summary>Respuesta con el tope diario alcanzado. Los accesos del menú siguen funcionando sin LLM.</summary>
    public const string RateLimitedReply =
        "Llegaste al límite de mensajes con DR. FLIT por hoy. Mañana puedes volver a escribirme; mientras tanto, " +
        "puedes buscar trámites, consultar el manual o generar un caso de soporte desde el menú.";

    /// <summary>Respuesta degradada. El frontend la acompaña con los resultados de su buscador local.</summary>
    public const string DegradedReply =
        "En este momento no puedo responderte con el asistente. Te muestro lo que encontré en el manual.";

    public async Task<DrFlitChatResult> AskAsync(DrFlitChatRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var limit = settings.DailyMessageLimit;
        var catalog = settings.Enabled ? catalogProvider.GetCatalog() : null;
        if (catalog is null)
        {
            var used = await counter.GetUsedTodayAsync(request.TenantId, request.UserId, ct).ConfigureAwait(false);
            DrFlitAssistantLog.Degraded(logger, request.TenantId, request.UserId, settings.Enabled ? "catalog_unavailable" : "disabled");
            return Degraded(used, limit);
        }

        var consumption = await counter
            .TryConsumeAsync(request.TenantId, request.UserId, limit, ct)
            .ConfigureAwait(false);
        if (!consumption.Allowed)
        {
            DrFlitAssistantLog.RateLimited(logger, request.TenantId, request.UserId, consumption.UsedToday, limit);
            return new DrFlitChatResult(DrFlitChatStatus.RateLimited, null, RateLimitedReply, [], consumption.UsedToday, limit);
        }

        var started = Stopwatch.GetTimestamp();
        var call = await model
            .CompleteAsync(catalog.SystemPrompt, BuildTurns(request), ct)
            .ConfigureAwait(false);
        var elapsedMs = (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds;

        var reply = call.Status == DrFlitModelCallStatus.Ok
            ? DrFlitModelReplyParser.Parse(call.Text, catalog.BySlug)
            : null;

        var usage = call.Usage;
        if (reply is null)
        {
            DrFlitAssistantLog.ModelFailed(
                logger, request.TenantId, request.UserId, call.Status, elapsedMs,
                usage?.InputTokens ?? 0, usage?.OutputTokens ?? 0);
            return Degraded(consumption.UsedToday, limit);
        }

        DrFlitAssistantLog.Answered(
            logger, request.TenantId, request.UserId, reply.Intent, elapsedMs,
            usage?.InputTokens ?? 0, usage?.OutputTokens ?? 0,
            usage?.CacheReadInputTokens ?? 0, usage?.CacheCreationInputTokens ?? 0);

        return new DrFlitChatResult(
            DrFlitChatStatus.Ok, reply.Intent, reply.Reply, reply.Citations, consumption.UsedToday, limit);
    }

    private static DrFlitChatResult Degraded(int usedToday, int limit) =>
        new(DrFlitChatStatus.Degraded, null, DegradedReply, [], usedToday, limit);

    /// <summary>
    /// Historial + mensaje nuevo. La conversación que ve el modelo empieza siempre con un turno del
    /// usuario: los saludos del bot que el cliente guarda al abrir el chat se descartan del inicio.
    /// </summary>
    private static List<DrFlitTurn> BuildTurns(DrFlitChatRequest request)
    {
        var turns = request.History
            .SkipWhile(t => t.Role == DrFlitTurnRole.Assistant)
            .ToList();
        turns.Add(new DrFlitTurn(DrFlitTurnRole.User, request.Message));
        return turns;
    }
}

/// <summary>Logging source-generated (CA1848) del chat. Sin texto del usuario ni del modelo.</summary>
internal static partial class DrFlitAssistantLog
{
    [LoggerMessage(Level = LogLevel.Information,
        Message = "DR. FLIT chat ok: tenant {TenantId} user {UserId} intent {Intent} latencia {ElapsedMs} ms tokens in {InputTokens} out {OutputTokens} cache_read {CacheRead} cache_write {CacheWrite}")]
    public static partial void Answered(
        ILogger logger, Guid tenantId, Guid userId, DrFlitIntent intent, long elapsedMs,
        int inputTokens, int outputTokens, int cacheRead, int cacheWrite);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "DR. FLIT chat degradado: tenant {TenantId} user {UserId} motivo {Reason}")]
    public static partial void Degraded(ILogger logger, Guid tenantId, Guid userId, string reason);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "DR. FLIT chat degradado por el modelo: tenant {TenantId} user {UserId} estado {CallStatus} latencia {ElapsedMs} ms tokens in {InputTokens} out {OutputTokens}")]
    public static partial void ModelFailed(
        ILogger logger, Guid tenantId, Guid userId, DrFlitModelCallStatus callStatus, long elapsedMs, int inputTokens, int outputTokens);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "DR. FLIT chat tope diario alcanzado: tenant {TenantId} user {UserId} usados {UsedToday}/{DailyLimit}")]
    public static partial void RateLimited(ILogger logger, Guid tenantId, Guid userId, int usedToday, int dailyLimit);
}
