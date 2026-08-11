using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Flit.Infrastructure.Notifications.Renting;

/// <summary>
/// HU #11360 — cuerpo del login: <c>{ secretName, subject }</c>. <see cref="SecretName"/> sale de
/// <c>RENTING_API_LOGIN_SECRET_NAME</c> y <see cref="Subject"/> de <c>RENTING_API_LOGIN_SUBJECT</c>
/// (el mismo Subject validado contra el certificado cliente en <see cref="RentingClientCertificateLoader"/>
/// — mTLS y login comparten identidad, HU #11359 AC5). Ninguno de los dos valores es, en sí mismo,
/// el secreto: <c>secretName</c> es solo el NOMBRE que el proveedor Renting resuelve del lado suyo.
/// </summary>
internal sealed record RentingLoginRequest(string SecretName, string Subject);

/// <summary>Respuesta cruda del login: <c>{ token, refresh }</c>. El proveedor NO informa expiración.</summary>
internal sealed record RentingLoginResponse(string? Token, string? Refresh);

/// <summary>
/// Token de login ya validado (no nulo/vacío). <see cref="Refresh"/> se conserva porque el
/// proveedor lo devuelve, pero esta HU no implementa un flujo de refresh — el TTL de la caché es
/// de configuración (<c>RENTING_API_LOGIN_CACHE_SECONDS_TTL</c>), no derivado de este campo.
/// </summary>
internal sealed record RentingLoginResult(string Token, string? Refresh);

/// <summary>Ejecuta el login del canal Renting contra <c>RENTING_API_LOGIN_PATH</c>.</summary>
internal interface IRentingLoginClient
{
    /// <exception cref="RentingAuthenticationException">
    /// El login fue rechazado, la respuesta no trae token, o el login agotó su propio tiempo de
    /// espera (<c>RENTING_API_LOGIN_SECONDS_TIMEOUT</c>). Nunca devuelve un resultado nulo/vacío
    /// tratado como éxito (HU #11360 AC6).
    /// </exception>
    Task<RentingLoginResult> LoginAsync(CancellationToken cancellationToken);
}

/// <summary>
/// HU #11360 — implementación HTTP del login. Usa el cliente nombrado
/// <see cref="RentingChannelOptions.HttpClientName"/> (certificado cliente + cabecera de
/// suscripción ya adjuntos por <c>InfrastructureExtensions.AddRentingChannel</c>, HU #11359): la
/// cabecera de suscripción viaja también en el login porque son dos credenciales distintas (API
/// key de transporte + Bearer de aplicación), tal como exige el contrato.
/// <para>
/// El timeout del login es PROPIO (<c>RENTING_API_LOGIN_SECONDS_TIMEOUT</c>), distinto del timeout
/// general de transporte del cliente HTTP: se acota con un <see cref="CancellationTokenSource"/>
/// enlazado en vez de tocar <c>HttpClient.Timeout</c> (que es un tope compartido por todas las
/// llamadas que usen ese cliente, incluido el envío de la HU #11361).
/// </para>
/// </summary>
internal sealed class RentingLoginClient(
    IHttpClientFactory httpClientFactory, IOptions<RentingChannelOptions> options) : IRentingLoginClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<RentingLoginResult> LoginAsync(CancellationToken cancellationToken)
    {
        var o = options.Value;
        var client = httpClientFactory.CreateClient(RentingChannelOptions.HttpClientName);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, o.LoginSecondsTimeout)));

        var payload = new RentingLoginRequest(o.LoginSecretName, o.LoginSubject);

        HttpResponseMessage response;
        try
        {
            response = await client.PostAsJsonAsync(o.LoginPath, payload, JsonOptions, timeoutCts.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // AC6 — el login agotó SU propio tiempo de espera: es un fallo de login (autenticación),
            // no el "tiempo agotado" del AC5 (que es del envío, y lo mapea el ejecutor por su cuenta).
            throw new RentingAuthenticationException(
                "Renting: el login agotó el tiempo de espera (RENTING_API_LOGIN_SECONDS_TIMEOUT).");
        }
        catch (HttpRequestException ex)
        {
            throw new RentingAuthenticationException("Renting: el login no pudo conectar con el proveedor.", ex);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                // AC6 — el fallo de login NO se silencia: se propaga siempre, nunca un token nulo
                // tratado como éxito parcial.
                throw new RentingAuthenticationException(
                    $"Renting: el login fue rechazado por el proveedor (HTTP {(int)response.StatusCode}).");
            }

            RentingLoginResponse? body;
            try
            {
                body = await response.Content.ReadFromJsonAsync<RentingLoginResponse>(JsonOptions, cancellationToken);
            }
            catch (JsonException ex)
            {
                throw new RentingAuthenticationException("Renting: la respuesta de login no es JSON válido.", ex);
            }

            if (string.IsNullOrWhiteSpace(body?.Token))
                throw new RentingAuthenticationException("Renting: el login no devolvió token.");

            return new RentingLoginResult(body.Token, body.Refresh);
        }
    }
}
