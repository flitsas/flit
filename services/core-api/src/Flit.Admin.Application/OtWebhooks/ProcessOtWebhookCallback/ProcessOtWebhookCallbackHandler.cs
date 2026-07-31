using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Flit.Admin.Domain.OtClientProcedures;
using Flit.Admin.Domain.OtWebhooks;

namespace Flit.Admin.Application.OtWebhooks.ProcessOtWebhookCallback;

public sealed class ProcessOtWebhookCallbackRequest
{
    public string EventType { get; init; } = string.Empty;

    public Guid ProcedureInstanceId { get; init; }

    public string ExternalStatus { get; init; } = string.Empty;

    public Guid? CorrelationId { get; init; }
}

public enum ProcessOtWebhookCallbackStatus
{
    Processed,
    NotFound,
    Inactive,
    InvalidSignature,
    InvalidPayload,
    InvalidState,
}

public sealed class ProcessOtWebhookCallbackResult
{
    public ProcessOtWebhookCallbackStatus Status { get; init; }

    public static ProcessOtWebhookCallbackResult Processed() =>
        new() { Status = ProcessOtWebhookCallbackStatus.Processed };

    public static ProcessOtWebhookCallbackResult NotFound() =>
        new() { Status = ProcessOtWebhookCallbackStatus.NotFound };

    public static ProcessOtWebhookCallbackResult Inactive() =>
        new() { Status = ProcessOtWebhookCallbackStatus.Inactive };

    public static ProcessOtWebhookCallbackResult InvalidSignature() =>
        new() { Status = ProcessOtWebhookCallbackStatus.InvalidSignature };

    public static ProcessOtWebhookCallbackResult InvalidPayload() =>
        new() { Status = ProcessOtWebhookCallbackStatus.InvalidPayload };

    public static ProcessOtWebhookCallbackResult InvalidState() =>
        new() { Status = ProcessOtWebhookCallbackStatus.InvalidState };
}

/// <summary>
/// Procesa callback inbound de una integración OT → FLIT (HU #10216 / RF04). El rechazo (HU #10871
/// AC2) transiciona a <c>subsanacion</c>, no a <c>rechazado</c>: es una observación subsanable.
/// </summary>
public sealed class ProcessOtWebhookCallbackHandler
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IOtWebhookSubscriptionRepository _subscriptionRepository;
    private readonly IOtApiCallLogRepository _apiCallLogRepository;
    private readonly IOtClientProcedureRepository _clientProcedureRepository;

    public ProcessOtWebhookCallbackHandler(
        IOtWebhookSubscriptionRepository subscriptionRepository,
        IOtApiCallLogRepository apiCallLogRepository,
        IOtClientProcedureRepository clientProcedureRepository)
    {
        _subscriptionRepository = subscriptionRepository
            ?? throw new ArgumentNullException(nameof(subscriptionRepository));
        _apiCallLogRepository = apiCallLogRepository
            ?? throw new ArgumentNullException(nameof(apiCallLogRepository));
        _clientProcedureRepository = clientProcedureRepository
            ?? throw new ArgumentNullException(nameof(clientProcedureRepository));
    }

    public async Task<ProcessOtWebhookCallbackResult> HandleAsync(
        Guid subscriptionId,
        string rawBody,
        string? signatureHeader,
        ProcessOtWebhookCallbackRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var sw = Stopwatch.StartNew();
        var subscription = await _subscriptionRepository
            .GetBySubscriptionIdAsync(subscriptionId, cancellationToken)
            .ConfigureAwait(false);

        if (subscription is null)
        {
            return ProcessOtWebhookCallbackResult.NotFound();
        }

        if (!subscription.IsActive)
        {
            await LogInboundAsync(
                subscription.TenantId,
                subscriptionId,
                rawBody,
                403,
                (int)sw.ElapsedMilliseconds,
                request.CorrelationId,
                cancellationToken).ConfigureAwait(false);
            return ProcessOtWebhookCallbackResult.Inactive();
        }

        if (!ValidateSignature(rawBody, signatureHeader, subscription.SecretHash))
        {
            await LogInboundAsync(
                subscription.TenantId,
                subscriptionId,
                rawBody,
                401,
                (int)sw.ElapsedMilliseconds,
                request.CorrelationId,
                cancellationToken).ConfigureAwait(false);
            return ProcessOtWebhookCallbackResult.InvalidSignature();
        }

        if (request.ProcedureInstanceId == Guid.Empty
            || string.IsNullOrWhiteSpace(request.ExternalStatus))
        {
            await LogInboundAsync(
                subscription.TenantId,
                subscriptionId,
                rawBody,
                422,
                (int)sw.ElapsedMilliseconds,
                request.CorrelationId,
                cancellationToken).ConfigureAwait(false);
            return ProcessOtWebhookCallbackResult.InvalidPayload();
        }

        var transitioned = await ApplyExternalStatusAsync(
            subscription.TenantId,
            request.ProcedureInstanceId,
            request.ExternalStatus,
            cancellationToken).ConfigureAwait(false);

        var statusCode = (short)(transitioned ? 200 : 409);
        await LogInboundAsync(
            subscription.TenantId,
            subscriptionId,
            rawBody,
            statusCode,
            (int)sw.ElapsedMilliseconds,
            request.CorrelationId,
            cancellationToken).ConfigureAwait(false);

        return transitioned
            ? ProcessOtWebhookCallbackResult.Processed()
            : ProcessOtWebhookCallbackResult.InvalidState();
    }

    private async Task<bool> ApplyExternalStatusAsync(
        Guid otTenantId,
        Guid procedureInstanceId,
        string externalStatus,
        CancellationToken cancellationToken)
    {
        var normalized = externalStatus.Trim().ToLowerInvariant();
        return normalized switch
        {
            // N 03: vocabulario nuevo en español + tokens legacy de integraciones externas.
            "aprobado" or "approved" or "approved_ot" => await ApproveIfPendingAsync(
                otTenantId, procedureInstanceId, cancellationToken).ConfigureAwait(false),
            "rechazado" or "rejected" or "rejected_ot" => await RejectIfPendingAsync(
                otTenantId, procedureInstanceId, "Rechazado vía integración Quipux", cancellationToken)
                .ConfigureAwait(false),
            _ => false,
        };
    }

    private async Task<bool> ApproveIfPendingAsync(
        Guid otTenantId,
        Guid procedureInstanceId,
        CancellationToken cancellationToken)
    {
        var result = await _clientProcedureRepository
            .ApproveAsync(
                otTenantId, procedureInstanceId, approvedBy: null,
                OtTransitionSource.QuipuxWebhook, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        return result is not null;
    }

    // HU #10871 (AC2) — el callback de rechazo de una integración registrada vía webhook (Quipux u
    // otra, p. ej. RNMC) transiciona a 'subsanacion' (no a 'rechazado'): el motivo de la integración
    // queda disponible en el checklist HÍBRIDO de metadata para que el gestor corrija y vuelva a
    // radicar sin perder el historial (N 03 / HU #10870). Sin checklist estructurado (items vacío):
    // el callback solo trae el motivo general.
    private async Task<bool> RejectIfPendingAsync(
        Guid otTenantId,
        Guid procedureInstanceId,
        string reason,
        CancellationToken cancellationToken)
    {
        var result = await _clientProcedureRepository
            .ObserveAsync(
                otTenantId, procedureInstanceId, reason, items: [], observedBy: null,
                OtTransitionSource.QuipuxWebhook, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        return result is not null;
    }

    private async Task LogInboundAsync(
        Guid tenantId,
        Guid subscriptionId,
        string rawBody,
        short responseCode,
        int durationMs,
        Guid? correlationId,
        CancellationToken cancellationToken)
    {
        var payloadHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawBody)))
            .ToLowerInvariant();

        await _apiCallLogRepository.AppendAsync(
            tenantId,
            direction: "inbound",
            endpoint: $"/api/v1/integrations/ot/webhooks/{subscriptionId}/callback",
            httpMethod: "POST",
            payloadHash,
            responseCode,
            durationMs,
            correlationId ?? Guid.NewGuid(),
            cancellationToken).ConfigureAwait(false);
    }

    private static bool ValidateSignature(string body, string? signatureHeader, string secretHashHex)
    {
        if (string.IsNullOrWhiteSpace(signatureHeader))
        {
            return false;
        }

        var signingKey = Convert.FromHexString(secretHashHex);
        var expected = ComputeHmacSha256Hex(body, signingKey);
        var provided = signatureHeader.Trim();
        if (provided.StartsWith("sha256=", StringComparison.OrdinalIgnoreCase))
        {
            provided = provided["sha256=".Length..];
        }

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expected),
            Encoding.UTF8.GetBytes(provided.ToLowerInvariant()));
    }

    private static string ComputeHmacSha256Hex(string body, byte[] signingKey)
    {
        using var hmac = new HMACSHA256(signingKey);
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(body));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
