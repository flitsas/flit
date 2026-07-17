using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Flit.Modules.Quipux.Domain.Configuracion;
using Flit.Modules.Quipux.Domain.Puertos;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Flit.Infrastructure.Quipux;

/// <summary>
/// Cliente HTTP de Quipux (login + radicación + consulta de estado). Reemplaza al microservicio
/// BackCrudQuipux de FLIT 1.0, que no tenía lógica de negocio: era login + subir a S3 + reenviar el POST.
/// Errores → <see cref="QuipuxException"/>, clasificados en transitorio (timeout, red, 5xx) vs. definitivo
/// (payload inválido, 4xx, respuesta ilegible), como <c>RuesApiClient</c> y <c>KyverumVerifyClient</c>.
///
/// URLs ABSOLUTAS POR LLAMADA (no <c>BaseAddress</c>): la configuración de Quipux vive en BD
/// (<c>admin.quipux_settings</c>), no en <c>appsettings</c>/<c>IOptions</c>, y es editable en caliente —
/// cambiar una URL o una credencial debe ser un UPDATE, sin desplegar. <c>HttpClient.BaseAddress</c> se
/// congela en el registro DI, así que resolver rutas relativas contra él haría que un cambio de URL en BD
/// solo surtiera efecto tras reiniciar. Por eso cada llamada relee los settings vía
/// <see cref="IQuipuxSettingsRepository"/> y postea contra la URL absoluta de la fila.
/// CAVEAT CONOCIDO: <c>HttpClient.Timeout</c> SÍ se fija en el registro DI desde
/// <see cref="QuipuxSettings.TimeoutSeconds"/> y por tanto NO es hot-reloadable (cambiarlo exige reinicio).
/// Se acepta: es un parámetro operativo que casi no se mueve, y meterlo por CTS propio partiría el
/// presupuesto de tiempo entre login y POST de forma menos predecible.
///
/// SEGURIDAD: nunca se loguean el token ni la password (en 1.0, :207, se hacía <c>console.warn</c> de los
/// headers de la petición, o sea del JWT en claro).
/// </summary>
internal sealed class QuipuxApiClient(
    HttpClient http,
    IQuipuxSettingsRepository settingsRepository,
    ILogger<QuipuxApiClient> logger) : IQuipuxClient
{
    /// <summary>
    /// <c>JsonSerializerDefaults.Web</c> activa <c>AllowReadingFromString</c>, así que un
    /// <c>"codigo": "81"</c> (string) también deserializa a int. Quipux no documenta el tipo y 1.0 lo
    /// comparaba con <c>===</c> tras un cast implícito de JS.
    /// </summary>
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// El caché es ESTÁTICO a propósito: <c>AddHttpClient&lt;IQuipuxClient, QuipuxApiClient&gt;</c> registra
    /// el typed client como TRANSIENT, así que un campo de instancia daría un caché nuevo por resolución y
    /// un login por request — justo lo que se quiere evitar. El token de Quipux es de proceso (una sola
    /// fila de configuración), no por-request. Mantenerlo aquí y no en un servicio inyectado evita además
    /// acoplar el registro DI a un tipo extra: este cliente solo necesita que se registre a sí mismo.
    /// </summary>
    private static readonly QuipuxTokenCache TokenCache = new();

    /// <summary>Margen antes del <c>exp</c> real, para no usar un token que vence en vuelo.</summary>
    private static readonly TimeSpan ExpirySkew = TimeSpan.FromSeconds(60);

    /// <summary>TTL conservador cuando el <c>exp</c> del JWT no se puede leer.</summary>
    private static readonly TimeSpan FallbackTtl = TimeSpan.FromSeconds(120);

    /// <summary>Piso de cacheo cuando el <c>exp</c> ya pasó (o cae dentro del margen): evita una tormenta de logins.</summary>
    private static readonly TimeSpan MinTtl = TimeSpan.FromSeconds(30);

    /// <inheritdoc />
    public async Task<QuipuxRegisterResult> RegisterDocumentAsync(
        QuipuxRegisterRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var settings = await LoadSettingsAsync(cancellationToken);
        var url = RequireUrl(settings.UrlRegisterDocument, "UrlRegisterDocument");

        // `contenidoDocumento` es la KEY S3 (`FLIT/{documento}.pdf`), NO el PDF en base64: Quipux lee el
        // objeto del bucket. La subida es responsabilidad de IQuipuxDocumentUploader, ANTES de llegar aquí.
        var body = new QuipuxRegisterBody(
            Placa: request.Placa,
            Vin: request.Vin,
            CodigoDivipo: request.CodigoDivipo,
            Consumidor: request.Consumidor,
            TipoTramite: request.TipoTramite,
            TipoRequisito: request.TipoRequisito,
            TipoDocumentoPropietario: request.TipoDocumentoPropietario,
            Documento: request.Documento,
            NroDocumentoPropietario: request.NroDocumentoPropietario,
            TipoDocumentoFuncionario: request.TipoDocumentoFuncionario,
            NroDocumentoFuncionario: request.NroDocumentoFuncionario,
            ContenidoDocumento: request.ContenidoDocumento);

        var payload = await SendAuthenticatedAsync<QuipuxRegisterBody, QuipuxRegisterResponse>(
            settings, url, body, "la radicación", cancellationToken);

        if (payload?.Data?.Codigo is not { } codigo)
            throw new QuipuxException("Respuesta inválida de Quipux al radicar el documento.", isTransient: false);

        return new QuipuxRegisterResult(codigo, payload.Data.Descripcion);
    }

    /// <inheritdoc />
    public async Task<QuipuxValidateStatusResult> ValidateStatusAsync(
        QuipuxValidateStatusRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var settings = await LoadSettingsAsync(cancellationToken);
        var url = RequireUrl(settings.UrlValidateStatus, "UrlValidateStatus");

        var body = new QuipuxValidateStatusBody(
            Documento: request.Documento,
            CodigoDivipo: request.CodigoDivipo,
            Consumidor: request.Consumidor);

        var payload = await SendAuthenticatedAsync<QuipuxValidateStatusBody, QuipuxValidateStatusResponse>(
            settings, url, body, "la consulta de estado", cancellationToken);

        if (payload?.Data?.Codigo is not { } codigo)
            throw new QuipuxException("Respuesta inválida de Quipux al consultar el estado.", isTransient: false);

        // `estadoTramite` AUSENTE ⇒ null, NO 0. 1.0 colapsaba el ausente a '0' y perdía la distinción entre
        // "Quipux aún no resolvió el trámite" y "Quipux respondió un estado 0", que son cosas distintas para
        // el sondeo (la primera se reintenta; la segunda es un dato real).
        var estado = payload.Data.EstadoTramite;
        return new QuipuxValidateStatusResult(codigo, payload.Data.Descripcion, estado?.Codigo, estado?.Descripcion);
    }

    // ── Transporte ────────────────────────────────────────────────────────────

    /// <summary>
    /// POST autenticado con reintento único ante 401.
    ///
    /// BUG 3 DE 1.0: allí <c>uploadDocument</c> vivía DENTRO de <c>registerDocument</c> (:182 vs :216), así
    /// que el reintento por 401 volvía a subir el PDF a S3 — y lo hacía con el payload ya MUTADO por el
    /// intento anterior, corrompiendo el objeto del bucket. Aquí la subida no es responsabilidad de este
    /// cliente y el reintento solo repite el POST: se invalida el token, se re-loguea y se reintenta UNA
    /// vez. <paramref name="body"/> es un record inmutable y no se toca entre intentos.
    /// </summary>
    private async Task<TResponse?> SendAuthenticatedAsync<TBody, TResponse>(
        QuipuxSettings settings,
        string url,
        TBody body,
        string operacion,
        CancellationToken ct)
    {
        try
        {
            var token = await GetTokenAsync(settings, staleToken: null, ct);

            using (var response = await PostAsync(url, body, token, ct))
            {
                if (response.StatusCode != HttpStatusCode.Unauthorized)
                    return await ReadAsync<TResponse>(response, operacion, ct);
            }

            // 401: el token cacheado ya no sirve (revocado, config cambiada, o venció antes de lo que decía
            // su `exp`). Se fuerza un login pasando el token podrido como `staleToken` para que, si hay
            // varias llamadas en el mismo 401, solo una re-loguee.
            QuipuxLog.TokenRechazado(logger, operacion);
            var refreshed = await GetTokenAsync(settings, staleToken: token, ct);

            using var retry = await PostAsync(url, body, refreshed, ct);
            // Si vuelve a dar 401, ReadAsync lo clasifica como DEFINITIVO (4xx): re-loguear otra vez no lo
            // arreglaría — son credenciales malas, no un token vencido.
            return await ReadAsync<TResponse>(retry, operacion, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (TaskCanceledException)
        {
            throw new QuipuxException($"Timeout en {operacion} contra Quipux.", isTransient: true);
        }
        catch (HttpRequestException ex)
        {
            throw new QuipuxException($"Error de red en {operacion} contra Quipux: {ex.Message}", isTransient: true);
        }
        catch (JsonException)
        {
            throw new QuipuxException($"No se pudo interpretar la respuesta de Quipux en {operacion}.", isTransient: false);
        }
    }

    /// <summary>
    /// POST con el header propietario <c>token</c>. OJO: Quipux NO usa <c>Authorization: Bearer</c>; espera
    /// un header llamado literalmente "token" con el JWT crudo (por eso va con
    /// <c>TryAddWithoutValidation</c>: no es un header estándar).
    /// </summary>
    private async Task<HttpResponseMessage> PostAsync<TBody>(string url, TBody body, string token, CancellationToken ct)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonContent.Create(body, options: JsonOptions),
        };
        message.Headers.TryAddWithoutValidation("token", token);
        return await http.SendAsync(message, ct);
    }

    /// <summary>Valida el status HTTP y deserializa. 5xx ⇒ transitorio; 4xx ⇒ definitivo.</summary>
    private async Task<TResponse?> ReadAsync<TResponse>(HttpResponseMessage response, string operacion, CancellationToken ct)
    {
        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await SafeReadBodyAsync(response, ct);
            var transient = (int)response.StatusCode >= 500;
            QuipuxLog.ErrorHttp(logger, operacion, (int)response.StatusCode, errorBody);
            throw new QuipuxException(
                transient
                    ? $"Quipux no disponible en {operacion} ({(int)response.StatusCode})."
                    : $"Quipux rechazó {operacion} ({(int)response.StatusCode}).",
                transient);
        }

        return await response.Content.ReadFromJsonAsync<TResponse>(JsonOptions, ct);
    }

    // ── Token ─────────────────────────────────────────────────────────────────

    /// <summary>Token vigente para estos settings, delegando el anti-estampida en <see cref="QuipuxTokenCache"/>.</summary>
    private Task<string> GetTokenAsync(QuipuxSettings settings, string? staleToken, CancellationToken ct)
    {
        // La clave incluye la identidad de la configuración (NUNCA la password): si en BD cambian la URL de
        // login, el usuario o el consumidor, el token viejo deja de reutilizarse solo, sin reiniciar.
        var cacheKey = $"{settings.UrlLogin}|{settings.Username}|{settings.ConsumerCode}";

        return TokenCache.GetOrRefreshAsync(
            cacheKey,
            staleToken,
            async token =>
            {
                var jwt = await LoginAsync(settings, token);
                return new QuipuxToken(jwt, CalcularVencimiento(jwt));
            },
            ct);
    }

    /// <summary>Login: <c>POST {UrlLogin}</c> con <c>{usuario, password, consumidor}</c> y solo Content-Type.</summary>
    private async Task<string> LoginAsync(QuipuxSettings settings, CancellationToken ct)
    {
        var url = RequireUrl(settings.UrlLogin, "UrlLogin");
        if (string.IsNullOrWhiteSpace(settings.Username) || string.IsNullOrWhiteSpace(settings.Password))
            throw new QuipuxException("Faltan las credenciales de Quipux en la configuración.", isTransient: false);

        var body = new QuipuxLoginBody(settings.Username, settings.Password, settings.ConsumerCode);

        using var message = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonContent.Create(body, options: JsonOptions),
        };
        // Sin header `token` — es la llamada que lo emite.
        using var response = await http.SendAsync(message, ct);

        if (!response.IsSuccessStatusCode)
        {
            // A diferencia del resto de operaciones, del login NO se loguea el cuerpo: es la única respuesta
            // que podría eco-ar las credenciales enviadas. Solo el status code.
            var transient = (int)response.StatusCode >= 500;
            QuipuxLog.LoginErrorHttp(logger, (int)response.StatusCode);
            throw new QuipuxException(
                transient
                    ? $"Quipux no disponible en el login ({(int)response.StatusCode})."
                    : $"Quipux rechazó el login ({(int)response.StatusCode}).",
                transient);
        }

        var payload = await response.Content.ReadFromJsonAsync<QuipuxLoginResponse>(JsonOptions, ct);
        if (payload is null || string.IsNullOrWhiteSpace(payload.Token))
            throw new QuipuxException("Quipux devolvió un login sin token.", isTransient: false);

        return payload.Token!;
    }

    /// <summary>
    /// Vencimiento efectivo del token, DERIVADO DEL JWT.
    ///
    /// BUG 2 DE 1.0 (:250): la respuesta del login de Quipux NO trae <c>expiresIn</c>, así que
    /// <c>tokenCache.set("authToken", token, authResponse.expiresIn || 1300)</c> caía SIEMPRE en el
    /// fallback y cacheaba 1300 s fijos — un número mágico sin ninguna relación con el <c>exp</c> real del
    /// JWT. Si Quipux acorta la vida del token, 1.0 sirve tokens muertos hasta agotar los 1300 s; si la
    /// alarga, re-loguea de más. Aquí se lee el claim <c>exp</c> y se resta un margen.
    /// </summary>
    private DateTimeOffset CalcularVencimiento(string jwt)
    {
        var exp = TryLeerExp(jwt);
        if (exp is null)
        {
            // No se pudo leer el `exp` (token opaco, JWT malformado, o sin el claim): TTL corto y Warning.
            // Nunca se cae al número mágico de 1.0 — mejor re-loguear de más que servir un token muerto.
            QuipuxLog.TokenSinExp(logger, (int)FallbackTtl.TotalSeconds);
            return DateTimeOffset.UtcNow.Add(FallbackTtl);
        }

        var vencimiento = exp.Value - ExpirySkew;
        var piso = DateTimeOffset.UtcNow.Add(MinTtl);
        if (vencimiento <= piso)
        {
            // El token nace ya vencido o casi (desfase de reloj, o Quipux emitiendo tokens muy cortos). No
            // se devuelve un vencimiento en el pasado porque eso haría login en CADA llamada; se cachea el
            // mínimo y, si Quipux lo rechaza, el reintento por 401 de SendAuthenticatedAsync lo cubre.
            QuipuxLog.TokenVidaCorta(logger, (int)MinTtl.TotalSeconds);
            return piso;
        }

        return vencimiento;
    }

    /// <summary>Lee el claim <c>exp</c> sin validar la firma (Quipux firma con RS256 y no publica la clave pública).</summary>
    private static DateTimeOffset? TryLeerExp(string jwt)
    {
        try
        {
            var token = new JsonWebToken(jwt);
            // `ValidTo` es DateTime.MinValue cuando el JWT no trae `exp`.
            return token.ValidTo == DateTime.MinValue
                ? null
                : new DateTimeOffset(DateTime.SpecifyKind(token.ValidTo, DateTimeKind.Utc));
        }
        catch (Exception ex) when (ex is ArgumentException or SecurityTokenException or FormatException)
        {
            return null;
        }
    }

    // ── Configuración ─────────────────────────────────────────────────────────

    /// <summary>
    /// Relee los settings desde BD en CADA llamada (son hot-reloadables y viven en una fila única).
    /// No se valida <c>EstaCompleta()</c> aquí a propósito: eso incluye el bucket y las llaves AWS, que son
    /// asunto del uploader, no de este cliente. El gate operativo (<c>Enabled</c> + fila completa) lo hace
    /// el worker antes de encolar; aquí solo se exige lo que este cliente necesita para hablar HTTP.
    /// </summary>
    private async Task<QuipuxSettings> LoadSettingsAsync(CancellationToken ct)
    {
        var settings = await settingsRepository.GetAsync(ct);
        if (settings is null)
            throw new QuipuxException("No hay configuración de Quipux registrada.", isTransient: false);
        return settings;
    }

    /// <summary>URL absoluta de los settings. Configuración incompleta ⇒ error definitivo, no reintentable.</summary>
    private static string RequireUrl(string? url, string campo)
    {
        if (string.IsNullOrWhiteSpace(url))
            throw new QuipuxException($"La configuración de Quipux no tiene {campo}.", isTransient: false);
        if (!Uri.TryCreate(url, UriKind.Absolute, out _))
            throw new QuipuxException($"La configuración de Quipux tiene {campo} inválida.", isTransient: false);
        return url;
    }

    /// <summary>Lee el cuerpo de error de Quipux (truncado) sin lanzar. Solo diagnóstico.</summary>
    private static async Task<string> SafeReadBodyAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            return body.Length > 500 ? body[..500] : body;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or OperationCanceledException)
        {
            return "(sin cuerpo)";
        }
    }

    // ── Contrato de cable de Quipux (nombres en español, tal cual los espera) ──
    // JsonPropertyName explícito en todos: el binder no puede depender de convenciones de nombres
    // (restricción AOT del repo, nada de reflexión dinámica) y estos nombres son contrato externo.
    // `status` (nivel raíz de las respuestas) se OMITE a propósito: 1.0 nunca lo consume — la verdad está
    // en `data.codigo` — y su tipo en el cable no está fijado, así que mapearlo solo añadiría un modo de
    // fallo. Las propiedades no mapeadas se ignoran al deserializar.

    private sealed record QuipuxLoginBody(
        [property: JsonPropertyName("usuario")] string Usuario,
        [property: JsonPropertyName("password")] string Password,
        [property: JsonPropertyName("consumidor")] string Consumidor);

    private sealed record QuipuxLoginResponse(
        [property: JsonPropertyName("token")] string? Token);

    private sealed record QuipuxRegisterBody(
        [property: JsonPropertyName("placa")] string Placa,
        [property: JsonPropertyName("vin")] string Vin,
        [property: JsonPropertyName("codigoDivipo")] string CodigoDivipo,
        [property: JsonPropertyName("consumidor")] string Consumidor,
        [property: JsonPropertyName("tipoTramite")] int TipoTramite,
        [property: JsonPropertyName("tipoRequisito")] int TipoRequisito,
        [property: JsonPropertyName("tipoDocumentoPropietario")] int TipoDocumentoPropietario,
        [property: JsonPropertyName("documento")] string Documento,
        [property: JsonPropertyName("nroDocumentoPropietario")] string NroDocumentoPropietario,
        [property: JsonPropertyName("tipoDocumentoFuncionario")] int TipoDocumentoFuncionario,
        [property: JsonPropertyName("nroDocumentoFuncionario")] string NroDocumentoFuncionario,
        // Key S3 (`FLIT/{documento}.pdf`), no el PDF: Quipux lo lee del bucket.
        [property: JsonPropertyName("contenidoDocumento")] string ContenidoDocumento);

    private sealed record QuipuxRegisterResponse(
        [property: JsonPropertyName("data")] QuipuxRegisterData? Data);

    private sealed record QuipuxRegisterData(
        [property: JsonPropertyName("codigo")] int? Codigo,
        [property: JsonPropertyName("descripcion")] string? Descripcion);

    private sealed record QuipuxValidateStatusBody(
        [property: JsonPropertyName("documento")] string Documento,
        [property: JsonPropertyName("codigoDivipo")] string CodigoDivipo,
        [property: JsonPropertyName("consumidor")] string Consumidor);

    private sealed record QuipuxValidateStatusResponse(
        [property: JsonPropertyName("data")] QuipuxValidateStatusData? Data);

    private sealed record QuipuxValidateStatusData(
        [property: JsonPropertyName("codigo")] int? Codigo,
        [property: JsonPropertyName("descripcion")] string? Descripcion,
        // Ausente mientras Quipux no resuelve el trámite ⇒ null (1.0 lo colapsaba a '0').
        [property: JsonPropertyName("estadoTramite")] QuipuxEstadoTramite? EstadoTramite);

    private sealed record QuipuxEstadoTramite(
        [property: JsonPropertyName("codigo")] int? Codigo,
        [property: JsonPropertyName("descripcion")] string? Descripcion);
}

/// <summary>
/// Logging source-generated (CA1848) del cliente Quipux. NINGÚN mensaje incluye el token ni la password:
/// en 1.0 (:207) se logueaban los headers de la petición en claro, o sea el JWT completo.
/// </summary>
internal static partial class QuipuxLog
{
    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Quipux respondió error HTTP {StatusCode} en {Operacion}. Cuerpo: {ErrorBody}")]
    public static partial void ErrorHttp(ILogger logger, string operacion, int statusCode, string errorBody);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "El login de Quipux falló con HTTP {StatusCode}.")]
    public static partial void LoginErrorHttp(ILogger logger, int statusCode);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Quipux rechazó el token (401) en {Operacion}; se re-loguea y se reintenta una vez.")]
    public static partial void TokenRechazado(ILogger logger, string operacion);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "No se pudo leer el claim 'exp' del token de Quipux; se cachea {TtlSegundos}s.")]
    public static partial void TokenSinExp(ILogger logger, int ttlSegundos);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "El token de Quipux vence dentro del margen de seguridad; se cachea el mínimo de {TtlSegundos}s.")]
    public static partial void TokenVidaCorta(ILogger logger, int ttlSegundos);
}
