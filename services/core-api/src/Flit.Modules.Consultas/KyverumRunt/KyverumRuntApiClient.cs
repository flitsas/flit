using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Flit.Infrastructure.Improntas;
using Flit.Tramites.Application.UseCases.Consultations;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Flit.Infrastructure.KyverumRunt;

/// <summary>
/// Cliente HTTP de consultas Kyverum RUNT (HU #10478). Envuelve <c>POST /v1/vehiculos:consultar</c>
/// (VIN o placa+documento) y <c>POST /v1/personas:consultar</c> (documento + tipoDocumento opcional),
/// ambos con scope <c>runt:read</c>. Comparte <b>solo la configuración</b> con
/// <see cref="ImprontaRuntClient"/> (<see cref="ImprontaRuntOptions"/> / env <c>KYVERUM_RUNT_*</c>) —
/// misma key/base URL, distinto endpoint/scope; NO refactoriza ni reutiliza el flujo de improntas.
/// <c>runt.kyverum.com</c> es un dominio DISTINTO de <c>verify.kyverum.com</c> (Kyverum Verify,
/// biometría). Errores se mapean a <see cref="KyverumRuntException"/> SIN incluir jamás la API key:
/// <list type="bullet">
/// <item><c>200 OK</c> + <c>ok:false</c> ⇒ no encontrado (no transitorio, <c>IsNotFound</c>).</item>
/// <item><c>UNAUTHORIZED</c>/<c>VALIDATION_ERROR</c>/4xx ⇒ definitivo.</item>
/// <item><c>UPSTREAM_UNAVAILABLE</c>/5xx/timeout/red ⇒ transitorio (reintentable por el llamador).</item>
/// </list>
/// </summary>
internal sealed class KyverumRuntApiClient(
    HttpClient http,
    IOptions<ImprontaRuntOptions> options,
    ILogger<KyverumRuntApiClient> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ImprontaRuntOptions _options = options.Value;

    /// <summary>
    /// Consulta un vehículo por VIN (prioritario) o por placa+documento. El request se arma según
    /// <see cref="KyverumRuntVehicleQuery"/>: si trae VIN se envía <c>{ vin }</c>; si no,
    /// <c>{ placa, documento, tipoDocumento? }</c>.
    /// </summary>
    public async Task<KyverumRuntVehicleResponse> ConsultarVehiculoAsync(
        KyverumRuntVehicleQuery query, CancellationToken ct = default) =>
        (await ConsultarVehiculoConCrudoAsync(query, ct)).Response;

    /// <summary>
    /// Igual que <see cref="ConsultarVehiculoAsync"/>, pero devuelve además el JSON <b>tal como llegó</b>
    /// (HU #11304, ADR-0041).
    /// </summary>
    /// <remarks>
    /// Sin el crudo no hay forma de saber qué mandó realmente el proveedor: cuando el DTO omite un
    /// campo, no queda ninguna evidencia de que venía en la respuesta. Eso convirtió un modelo
    /// deducido de fixtures en profecía autocumplida, y es lo que dejó seis celdas del certificado sin
    /// una sola fila en toda la base. Guardarlo permite corregir el mapeo y reprocesar sin volver a
    /// pagar la consulta.
    ///
    /// <para>El crudo se sanitiza antes de persistirse (<c>RawPayloadSanitizer</c>) y <b>no</b> se
    /// vuelca en trazas ni logs.</para>
    /// </remarks>
    public async Task<(KyverumRuntVehicleResponse Response, string Raw)> ConsultarVehiculoConCrudoAsync(
        KyverumRuntVehicleQuery query, CancellationToken ct = default)
    {
        object body = !string.IsNullOrWhiteSpace(query.Vin)
            ? new VehiculoByVinBody(query.Vin!)
            : new VehiculoByPlacaBody(query.Placa!, query.Documento!, query.TipoDocumento);

        return await SendWithRawAsync<KyverumRuntVehicleResponse>(
            "/v1/vehiculos:consultar", body, r => r.Ok, ct);
    }

    /// <summary>
    /// Consulta una persona/conductor por documento (+ tipoDocumento opcional). Request
    /// <c>{ documento, tipoDocumento? }</c>.
    /// </summary>
    public async Task<KyverumRuntPersonaResponse> ConsultarPersonaAsync(
        KyverumRuntPersonaQuery query, CancellationToken ct = default)
    {
        var body = new PersonaBody(query.Documento, query.TipoDocumento);

        return await SendAsync<KyverumRuntPersonaResponse>(
            "/v1/personas:consultar", body, r => r.Ok, ct);
    }

    /// <summary>
    /// POST genérico a un endpoint de consulta Kyverum RUNT. Deserializa a <typeparamref name="T"/>,
    /// clasifica errores de transporte/negocio en <see cref="KyverumRuntException"/> y trata
    /// <c>ok:false</c> como "no encontrado". Nunca filtra la API key.
    /// </summary>
    private async Task<T> SendAsync<T>(string path, object body, Func<T, bool> isOk, CancellationToken ct)
        where T : class =>
        (await SendWithRawAsync(path, body, isOk, ct)).Response;

    private async Task<(T Response, string Raw)> SendWithRawAsync<T>(
        string path, object body, Func<T, bool> isOk, CancellationToken ct)
        where T : class
    {
        try
        {
            using var message = new HttpRequestMessage(HttpMethod.Post, path)
            {
                Content = JsonContent.Create(body, body.GetType(), options: JsonOptions),
            };
            if (!string.IsNullOrWhiteSpace(_options.ApiKey))
                message.Headers.Authorization = new AuthenticationHeaderValue(_options.AuthScheme, _options.ApiKey);
            message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            using var response = await http.SendAsync(message, ct);

            if (!response.IsSuccessStatusCode)
                throw await BuildErrorAsync(response, ct);

            // HU #11304 — se lee el cuerpo como texto y se deserializa desde ahí, en vez de
            // ReadFromJsonAsync directo: es la única forma de conservar el JSON tal como llegó, con
            // los campos que el DTO no declara incluidos. Sin eso no hay evidencia de qué manda el
            // proveedor y un mapeo equivocado es irreparable.
            var raw = await response.Content.ReadAsStringAsync(ct);
            var payload = JsonSerializer.Deserialize<T>(raw, JsonOptions);

            // 200 OK con ok:false: el RUNT no tiene el dato (vehículo/persona no encontrado). No es un
            // error HTTP — se marca IsNotFound para que el provider lo mapee a un check de "no hallado"
            // en vez de un "error" de proveedor.
            if (payload is null)
                throw new KyverumRuntException("Respuesta inválida de Kyverum RUNT.", isTransient: false);

            if (!isOk(payload))
            {
                var providerMessage = ExtractOkFalseMessage(payload);
                throw new KyverumRuntException(providerMessage, isTransient: false, isNotFound: true);
            }

            return (payload, raw);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (TaskCanceledException)
        {
            throw new KyverumRuntException("Timeout consultando Kyverum RUNT.", isTransient: true);
        }
        catch (HttpRequestException ex)
        {
            throw new KyverumRuntException($"Error de red consultando Kyverum RUNT: {ex.Message}", isTransient: true);
        }
        catch (JsonException)
        {
            throw new KyverumRuntException("No se pudo interpretar la respuesta de Kyverum RUNT.", isTransient: false);
        }
    }

    private static string ExtractOkFalseMessage<T>(T payload) => payload switch
    {
        KyverumRuntVehicleResponse v when !string.IsNullOrWhiteSpace(v.Message) => v.Message!,
        KyverumRuntPersonaResponse p when !string.IsNullOrWhiteSpace(p.Message) => p.Message!,
        _ => "Kyverum RUNT no encontró el registro consultado.",
    };

    /// <summary>
    /// Mapea una respuesta de error HTTP de Kyverum RUNT a <see cref="KyverumRuntException"/>. El
    /// código del cuerpo (no el status) determina la transitoriedad: <c>UPSTREAM_UNAVAILABLE</c> o
    /// cualquier 5xx es transitorio; <c>VALIDATION_ERROR</c>/<c>UNAUTHORIZED</c>/desconocido no lo es.
    /// El 401 es ambiguo (key inválida vs scope insuficiente): se inspecciona <c>error.message</c>.
    /// </summary>
    private async Task<KyverumRuntException> BuildErrorAsync(HttpResponseMessage response, CancellationToken ct)
    {
        var statusCode = (int)response.StatusCode;
        var errorBody = await SafeReadBodyAsync(response, ct);
        var envelope = TryParseError(errorBody);
        var code = envelope?.Error?.Code ?? "UNKNOWN";
        var providerMessage = envelope?.Error?.Message ?? "Error desconocido de Kyverum RUNT.";
        var transient = string.Equals(code, "UPSTREAM_UNAVAILABLE", StringComparison.Ordinal) || statusCode >= 500;

        // Nunca se loguea la API key ni el cuerpo crudo completo — solo código/mensaje del proveedor.
        KyverumRuntLog.ProviderError(logger, statusCode, code, providerMessage);

        var exceptionMessage = code switch
        {
            "VALIDATION_ERROR" => $"Kyverum RUNT rechazó la consulta: {providerMessage}",
            "UNAUTHORIZED" => $"Kyverum RUNT rechazó la autenticación: {providerMessage}",
            "UPSTREAM_UNAVAILABLE" => $"Kyverum RUNT no disponible temporalmente: {providerMessage}",
            _ => $"Kyverum RUNT respondió error ({statusCode}): {providerMessage}",
        };

        return new KyverumRuntException(exceptionMessage, transient);
    }

    private static KyverumRuntErrorEnvelope? TryParseError(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return null;

        try
        {
            return JsonSerializer.Deserialize<KyverumRuntErrorEnvelope>(body, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Lee el cuerpo de error del proveedor (truncado) sin lanzar. Solo para diagnóstico.</summary>
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

    // ── Contrato Kyverum RUNT (CONTRATO-API.md) ──────────────────────────────────────────────
    private sealed record VehiculoByVinBody(
        [property: JsonPropertyName("vin")] string Vin);

    private sealed record VehiculoByPlacaBody(
        [property: JsonPropertyName("placa")] string Placa,
        [property: JsonPropertyName("documento")] string Documento,
        [property: JsonPropertyName("tipoDocumento"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? TipoDocumento);

    private sealed record PersonaBody(
        [property: JsonPropertyName("documento")] string Documento,
        [property: JsonPropertyName("tipoDocumento"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? TipoDocumento);

    private sealed record KyverumRuntErrorEnvelope(
        [property: JsonPropertyName("error")] KyverumRuntErrorBody? Error);

    private sealed record KyverumRuntErrorBody(
        [property: JsonPropertyName("code")] string? Code,
        [property: JsonPropertyName("message")] string? Message,
        [property: JsonPropertyName("traceId")] string? TraceId);
}

/// <summary>Parámetros de consulta de vehículo. VIN prioritario; en su ausencia, placa+documento.</summary>
internal sealed record KyverumRuntVehicleQuery(string? Vin, string? Placa, string? Documento, string? TipoDocumento);

/// <summary>Parámetros de consulta de persona/conductor.</summary>
internal sealed record KyverumRuntPersonaQuery(string Documento, string? TipoDocumento);

/// <summary>Logging source-generated (CA1848) del cliente de consultas Kyverum RUNT. Nunca incluye la API key.</summary>
internal static partial class KyverumRuntLog
{
    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Kyverum RUNT (consultas) respondió error HTTP {StatusCode} código {ErrorCode}: {ErrorMessage}")]
    public static partial void ProviderError(ILogger logger, int statusCode, string errorCode, string errorMessage);
}
