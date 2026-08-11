using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Flit.Modules.Security.Domain.Auth;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Flit.Infrastructure.Notifications.Renting;

/// <summary>
/// Cuerpo crudo de la respuesta del proveedor: <c>{ statusCode, isSuccessStatusCode }</c>. Es un
/// acuse de ACEPTACIÓN del envío, no un identificador de mensaje ni confirmación de entrega al
/// destinatario (AC3).
/// </summary>
internal sealed record RentingSendEmailResponseBody(int? StatusCode, bool? IsSuccessStatusCode);

/// <summary>Puerto del adaptador de envío del canal Renting (HU #11361).</summary>
public interface IRentingEmailApiSender
{
    Task<EmailSendResult> SendAsync(RentingSendEmailRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// HU #11361 — adaptador de envío del canal Renting: construye el <c>multipart/form-data</c> con
/// el contrato EXACTO del proveedor (AC1/AC2) y mapea la respuesta al catálogo cerrado de
/// <see cref="EmailSendOutcome"/> (AC3/AC4/AC6), saneando el cuerpo de error antes de loguearlo
/// (AC5). Reutiliza <see cref="RentingAuthenticatedRequestExecutor"/> (HU #11360) para login,
/// caché de token y reintento ante 401 — esta clase NO reimplementa nada de eso: solo construye la
/// petición YA AUTENTICADA (recibe el token Bearer vigente) y mapea la respuesta.
/// </summary>
internal sealed class RentingEmailApiSender(
    RentingAuthenticatedRequestExecutor executor,
    IHttpClientFactory httpClientFactory,
    IOptions<RentingChannelOptions> options,
    IRentingRecipientOverride recipientOverride,
    ILogger<RentingEmailApiSender> logger) : IRentingEmailApiSender
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<EmailSendResult> SendAsync(RentingSendEmailRequest request, CancellationToken cancellationToken)
    {
        // Punto de encaje de la HU #11364 (desvío de destinatario fuera de producción). Esta HU
        // solo modela el hueco: por defecto (PassthroughRentingRecipientOverride) no cambia nada.
        var effective = recipientOverride.Apply(request);

        string? tokenUsed = null;
        var outcome = await executor.ExecuteAsync(
            async (token, ct) =>
            {
                tokenUsed = token;
                return await SendOnceAsync(effective, token, ct).ConfigureAwait(false);
            },
            cancellationToken).ConfigureAwait(false);

        if (outcome.IsFailure)
            return outcome.Failure!;

        using var response = outcome.Response!;
        return await MapResponseAsync(response, tokenUsed, cancellationToken).ConfigureAwait(false);
    }

    private async Task<HttpResponseMessage> SendOnceAsync(
        RentingSendEmailRequest request, string token, CancellationToken cancellationToken)
    {
        var o = options.Value;
        var client = httpClientFactory.CreateClient(RentingChannelOptions.HttpClientName);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, o.SendEmailSecondsTimeout)));

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, o.SendEmailPath)
        {
            Content = BuildMultipartContent(request),
        };
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        return await client.SendAsync(httpRequest, timeoutCts.Token).ConfigureAwait(false);
    }

    /// <summary>
    /// AC1/AC2 — construye el <c>multipart/form-data</c> con el contrato EXACTO del proveedor:
    /// <c>Subject</c>, <c>Body</c>, <c>Sender.Email</c>, <c>Sender.UserName</c>,
    /// <c>Recipients[i].Email</c>/<c>Recipients[i].UserName</c> por cada destinatario CON ÍNDICE
    /// INCREMENTAL, <c>bccRecipients[i].Email</c> por cada copia oculta CON SU PROPIO ÍNDICE
    /// incremental (a diferencia del defecto de la implementación de referencia del cliente, que
    /// escribe siempre en el índice 0 y pierde todos menos el último), y <c>AttachFiles</c> — este
    /// último campo SIEMPRE está presente en el cuerpo, incluso sin adjuntos (parte vacía), porque
    /// el contrato lo exige como campo, no solo como valor condicional.
    /// </summary>
    internal static MultipartFormDataContent BuildMultipartContent(RentingSendEmailRequest request)
    {
        var content = new MultipartFormDataContent
        {
            { new StringContent(request.Subject ?? string.Empty), "Subject" },
            { new StringContent(request.Body ?? string.Empty), "Body" },
            { new StringContent(request.Sender.Email ?? string.Empty), "Sender.Email" },
            { new StringContent(request.Sender.UserName ?? string.Empty), "Sender.UserName" },
        };

        for (var i = 0; i < request.Recipients.Count; i++)
        {
            content.Add(new StringContent(request.Recipients[i].Email ?? string.Empty), $"Recipients[{i}].Email");
            content.Add(new StringContent(request.Recipients[i].UserName ?? string.Empty), $"Recipients[{i}].UserName");
        }

        // AC2 — cada correo en copia oculta viaja en SU PROPIO índice incremental (i, no 0 fijo).
        for (var i = 0; i < request.BccRecipients.Count; i++)
            content.Add(new StringContent(request.BccRecipients[i] ?? string.Empty), $"bccRecipients[{i}].Email");

        if (request.Attachments.Count == 0)
        {
            content.Add(new StringContent(string.Empty), "AttachFiles");
        }
        else
        {
            foreach (var attachment in request.Attachments)
            {
                var fileContent = new ByteArrayContent(attachment.Content);
                fileContent.Headers.ContentType = new MediaTypeHeaderValue(
                    string.IsNullOrWhiteSpace(attachment.ContentType) ? "application/octet-stream" : attachment.ContentType);
                content.Add(fileContent, "AttachFiles", attachment.FileName);
            }
        }

        return content;
    }

    /// <summary>
    /// AC3/AC4/AC6 — mapea la respuesta HTTP al catálogo cerrado de <see cref="EmailSendOutcome"/>.
    /// 401 (defensivo: el ejecutor ya resuelve el 401 con reintento — HU #11360 — pero esta función
    /// es autocontenida) → <see cref="EmailSendOutcome.AuthenticationFailed"/>; 400 →
    /// <see cref="EmailSendOutcome.ContentRejected"/>; 429 → <see cref="EmailSendOutcome.RateLimited"/>;
    /// 503 (o cualquier otro código de error no enumerado) → <see cref="EmailSendOutcome.ProviderUnavailable"/>.
    /// Con éxito HTTP, exige que el cuerpo cumpla el contrato <c>{ isSuccessStatusCode }</c>: si no
    /// es JSON válido o le falta ese campo, es una respuesta ILEGIBLE (AC6) → ProviderUnavailable.
    /// </summary>
    private async Task<EmailSendResult> MapResponseAsync(
        HttpResponseMessage response, string? tokenUsed, CancellationToken cancellationToken)
    {
        switch (response.StatusCode)
        {
            case HttpStatusCode.Unauthorized:
                return EmailSendResult.Failed(EmailSendOutcome.AuthenticationFailed);
            case HttpStatusCode.BadRequest:
                await LogRejectionAsync(response, EmailSendOutcome.ContentRejected, tokenUsed, cancellationToken)
                    .ConfigureAwait(false);
                return EmailSendResult.Failed(EmailSendOutcome.ContentRejected);
            case HttpStatusCode.TooManyRequests:
                await LogRejectionAsync(response, EmailSendOutcome.RateLimited, tokenUsed, cancellationToken)
                    .ConfigureAwait(false);
                return EmailSendResult.Failed(EmailSendOutcome.RateLimited);
        }

        if (!response.IsSuccessStatusCode)
        {
            // 503 y cualquier otro código de error no enumerado explícitamente (AC4).
            await LogRejectionAsync(response, EmailSendOutcome.ProviderUnavailable, tokenUsed, cancellationToken)
                .ConfigureAwait(false);
            return EmailSendResult.Failed(EmailSendOutcome.ProviderUnavailable);
        }

        string raw;
        RentingSendEmailResponseBody? body;
        try
        {
            raw = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            body = JsonSerializer.Deserialize<RentingSendEmailResponseBody>(raw, JsonOptions);
        }
        catch (JsonException)
        {
            // AC6 — cuerpo ilegible: no corresponde al contrato.
            RentingEmailApiSenderLog.UnreadableResponse(logger);
            return EmailSendResult.Failed(EmailSendOutcome.ProviderUnavailable);
        }

        if (body?.IsSuccessStatusCode is not { } isSuccess)
        {
            // AC6 — JSON válido pero sin el campo del contrato: tampoco es una respuesta legible.
            RentingEmailApiSenderLog.UnreadableResponse(logger);
            return EmailSendResult.Failed(EmailSendOutcome.ProviderUnavailable);
        }

        // AC3 — es un acuse de ACEPTACIÓN del proveedor, nunca confirmación de entrega.
        return isSuccess ? EmailSendResult.Sent : EmailSendResult.Failed(EmailSendOutcome.ProviderUnavailable);
    }

    /// <summary>
    /// AC5 — el cuerpo de error se sanea (clave de API, token Bearer usado, nombre del secreto de
    /// login y passphrase NUNCA aparecen en el log) y se trunca a
    /// <see cref="RentingChannelOptions.SendEmailErrorBodyMaxLength"/> caracteres. El saneo corre
    /// ANTES del truncado: así un secreto que cruce el límite de longitud no queda parcialmente
    /// expuesto por un corte a mitad de cadena.
    /// </summary>
    private async Task LogRejectionAsync(
        HttpResponseMessage response, EmailSendOutcome outcome, string? tokenUsed, CancellationToken cancellationToken)
    {
        var o = options.Value;
        string raw;
        try
        {
            raw = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or ObjectDisposedException)
        {
            raw = string.Empty;
        }

        var sanitized = RentingErrorBodySanitizer.Sanitize(
            raw, o.SendEmailErrorBodyMaxLength, [o.ApiKeyValue, tokenUsed, o.LoginSecretName, o.Passphrase]);

        RentingEmailApiSenderLog.ProviderRejected(logger, (int)response.StatusCode, outcome, sanitized);
    }
}

/// <summary>
/// HU #11361 AC5 — sanea (redacta secretos) y trunca el cuerpo de error del proveedor ANTES de que
/// pueda llegar a un log o a la bitácora de notificaciones. Utilidad pura, sin estado.
/// </summary>
internal static class RentingErrorBodySanitizer
{
    private const string Redacted = "***";

    public static string Sanitize(string body, int maxLength, IEnumerable<string?> secrets)
    {
        var sanitized = body ?? string.Empty;

        foreach (var secret in secrets)
        {
            if (!string.IsNullOrEmpty(secret))
                sanitized = sanitized.Replace(secret, Redacted, StringComparison.Ordinal);
        }

        var bound = Math.Max(0, maxLength);
        return sanitized.Length > bound ? sanitized[..bound] : sanitized;
    }
}

/// <summary>Logging source-generated (CA1848) del adaptador de envío del canal Renting.</summary>
internal static partial class RentingEmailApiSenderLog
{
    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Canal Renting: el proveedor rechazó el envío (HTTP {StatusCode}, causa {Outcome}). Cuerpo: {SanitizedBody}")]
    public static partial void ProviderRejected(ILogger logger, int statusCode, EmailSendOutcome outcome, string sanitizedBody);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Canal Renting: la respuesta del proveedor no corresponde al contrato esperado (cuerpo ilegible).")]
    public static partial void UnreadableResponse(ILogger logger);
}
