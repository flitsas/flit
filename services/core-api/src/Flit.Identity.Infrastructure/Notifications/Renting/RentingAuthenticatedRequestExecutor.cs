using System.Net;
using Flit.Modules.Security.Domain.Auth;
using Microsoft.Extensions.Logging;

namespace Flit.Infrastructure.Notifications.Renting;

/// <summary>
/// Resultado de <see cref="RentingAuthenticatedRequestExecutor.ExecuteAsync"/>: o bien la
/// respuesta HTTP exitosa (status distinto de 401 — la HU #11361 decide qué códigos son éxito de
/// negocio y cuáles error de envío/contenido), o bien un fallo YA MAPEADO a
/// <see cref="EmailSendOutcome"/> (autenticación o tiempo agotado — AC4/AC5/AC6 de la HU #11360).
/// Exactamente una de las dos propiedades es no nula.
/// </summary>
internal sealed class RentingRequestOutcome
{
    private RentingRequestOutcome(HttpResponseMessage? response, EmailSendResult? failure)
    {
        Response = response;
        Failure = failure;
    }

    public HttpResponseMessage? Response { get; }

    public EmailSendResult? Failure { get; }

    public bool IsFailure => Failure is not null;

    public static RentingRequestOutcome Success(HttpResponseMessage response) => new(response, failure: null);

    public static RentingRequestOutcome AuthenticationFailed() =>
        new(response: null, EmailSendResult.Failed(EmailSendOutcome.AuthenticationFailed));

    public static RentingRequestOutcome TimedOut() =>
        new(response: null, EmailSendResult.Failed(EmailSendOutcome.TimedOut));
}

/// <summary>
/// HU #11360 — componente de autenticación + política de reintento del canal Renting, para que la
/// HU #11361 (adaptador de envío/multipart) lo reutilice SIN reimplementar login, caché ni
/// reintento. <see cref="ExecuteAsync"/> recibe una fábrica de la petición YA CONSTRUIDA
/// (multipart, headers de negocio, etc. — eso es responsabilidad de #11361) parametrizada por el
/// token Bearer vigente, y se limita a:
/// <list type="number">
/// <item>Obtener el token (login solo si hace falta — <see cref="IRentingTokenCache"/>).</item>
/// <item>Ejecutar <paramref name="send"/> con ese token.</item>
/// <item>
/// AC4 — si la respuesta es 401: invalida la caché, loguea de nuevo y reintenta UNA sola vez. Si
/// el segundo intento también da 401, falla con <see cref="EmailSendOutcome.AuthenticationFailed"/>
/// SIN un tercer intento.
/// </item>
/// <item>
/// AC5 — si <paramref name="send"/> agota el tiempo (cancelación NO originada en
/// <paramref name="cancellationToken"/> del llamador), falla con
/// <see cref="EmailSendOutcome.TimedOut"/> SIN reintentar.
/// </item>
/// <item>
/// AC6 — si el login (inicial o de reintento) falla, falla con
/// <see cref="EmailSendOutcome.AuthenticationFailed"/>; nunca se trata un token nulo como éxito.
/// </item>
/// </list>
/// </summary>
internal sealed class RentingAuthenticatedRequestExecutor(
    IRentingTokenCache tokenCache, ILogger<RentingAuthenticatedRequestExecutor> logger)
{
    /// <param name="send">
    /// Construye Y ejecuta la petición HTTP con el token Bearer recibido. NO debe atrapar
    /// <see cref="OperationCanceledException"/> ni <see cref="HttpRequestException"/> — este
    /// ejecutor las interpreta (timeout vs. error de transporte). Responsabilidad de la HU #11361.
    /// </param>
    public async Task<RentingRequestOutcome> ExecuteAsync(
        Func<string, CancellationToken, Task<HttpResponseMessage>> send, CancellationToken cancellationToken)
    {
        string token;
        try
        {
            token = await tokenCache.GetTokenAsync(cancellationToken);
        }
        catch (RentingAuthenticationException ex)
        {
            RentingChannelExecutorLog.LoginFailed(logger, ex.Message);
            return RentingRequestOutcome.AuthenticationFailed();
        }

        var first = await SendOnceAsync(send, token, cancellationToken);
        if (first.TimedOut)
            return RentingRequestOutcome.TimedOut();
        if (first.Response is { } okResponse && okResponse.StatusCode != HttpStatusCode.Unauthorized)
            return RentingRequestOutcome.Success(okResponse);

        // AC4 — 401: invalida la caché (si otra llamada concurrente ya la refrescó, esto es un
        // no-op) y reintenta UNA sola vez con un login nuevo.
        first.Response?.Dispose();
        tokenCache.Invalidate(token);
        RentingChannelExecutorLog.RetryAfterUnauthorized(logger);

        string retryToken;
        try
        {
            retryToken = await tokenCache.GetTokenAsync(cancellationToken);
        }
        catch (RentingAuthenticationException ex)
        {
            RentingChannelExecutorLog.LoginFailed(logger, ex.Message);
            return RentingRequestOutcome.AuthenticationFailed();
        }

        var second = await SendOnceAsync(send, retryToken, cancellationToken);
        if (second.TimedOut)
            return RentingRequestOutcome.TimedOut();

        if (second.Response is { } secondResponse && secondResponse.StatusCode == HttpStatusCode.Unauthorized)
        {
            secondResponse.Dispose();
            RentingChannelExecutorLog.SecondUnauthorized(logger);
            return RentingRequestOutcome.AuthenticationFailed();
        }

        // AC4 — el proveedor NO recibe un tercer intento bajo ninguna circunstancia: si el segundo
        // intento vuelve a dar 401, ya se devolvió arriba.
        return RentingRequestOutcome.Success(second.Response!);
    }

    private static async Task<(HttpResponseMessage? Response, bool TimedOut)> SendOnceAsync(
        Func<string, CancellationToken, Task<HttpResponseMessage>> send, string token, CancellationToken cancellationToken)
    {
        try
        {
            var response = await send(token, cancellationToken);
            return (response, TimedOut: false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // AC5 — el envío agotó SU tiempo de espera (no la cancelación del llamador): sin
            // reintento, causa TimedOut.
            return (null, TimedOut: true);
        }
    }
}

/// <summary>Logging source-generated (CA1848) del ejecutor autenticado del canal Renting.</summary>
internal static partial class RentingChannelExecutorLog
{
    [LoggerMessage(Level = LogLevel.Warning, Message = "Canal Renting: login falló ({Reason}).")]
    public static partial void LoginFailed(ILogger logger, string reason);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Canal Renting: envío rechazado con 401, invalidando token y reintentando una vez.")]
    public static partial void RetryAfterUnauthorized(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error,
        Message = "Canal Renting: el reintento también recibió 401; no se hace un tercer intento.")]
    public static partial void SecondUnauthorized(ILogger logger);
}
