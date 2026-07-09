using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Flit.Modules.Improntas.Domain;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Flit.Infrastructure.Improntas;

/// <summary>
/// Cliente HTTP de Kyverum RUNT (HU #10465). Envuelve <c>POST /v1/improntas:generar</c> (requiere
/// scope <c>impronta:generar</c>) y devuelve el certificado ya parseado (PDF como Data URI base64,
/// hash, radicado, fechaImpresa). Replica el patrón de <c>KyverumVerifyClient</c>
/// (<c>Flit.Infrastructure.Kyverum</c>) para el dominio <c>runt.kyverum.com</c> (distinto de
/// <c>verify.kyverum.com</c>). Errores se mapean a <see cref="ImprontaRuntException"/> SIN incluir
/// nunca la API key:
/// <list type="bullet">
/// <item><c>VALIDATION_ERROR</c> ⇒ no transitorio, con el detalle de campos inválidos.</item>
/// <item><c>UNAUTHORIZED</c> ⇒ no transitorio; el proveedor no distingue key inválida de scope
/// insuficiente por status code (siempre 401) — se inspecciona <c>error.message</c> del cuerpo.</item>
/// <item><c>UPSTREAM_UNAVAILABLE</c> (502) / 5xx / timeout / red ⇒ transitorio, reintentable con
/// backoff por el llamador.</item>
/// </list>
/// </summary>
internal sealed class ImprontaRuntClient(
    HttpClient http,
    IOptions<ImprontaRuntOptions> options,
    ILogger<ImprontaRuntClient> logger) : IImprontaExternalClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ImprontaRuntOptions _options = options.Value;

    public async Task<ImprontaExternalResult> GenerarAsync(ImprontaExternalRequest request, CancellationToken ct = default)
    {
        var body = new ImprontaRuntRequestBody(
            Placa: request.Placa,
            Documento: request.Documento,
            NumMotor: request.NumMotor,
            NumChasis: request.NumChasis,
            NumSerie: request.NumSerie,
            Marca: request.Marca,
            Linea: request.Linea,
            Modelo: request.Modelo,
            OrgNombre: request.OrgNombre,
            OrgNit: request.OrgNit,
            OrgCiudad: request.OrgCiudad,
            Operador: request.Operador,
            Vin: request.Vin);

        try
        {
            using var message = new HttpRequestMessage(HttpMethod.Post, "/v1/improntas:generar")
            {
                Content = JsonContent.Create(body, options: JsonOptions),
            };
            if (!string.IsNullOrWhiteSpace(_options.ApiKey))
                message.Headers.Authorization = new AuthenticationHeaderValue(_options.AuthScheme, _options.ApiKey);
            message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            using var response = await http.SendAsync(message, ct);

            if (!response.IsSuccessStatusCode)
                throw await BuildErrorAsync(response, ct);

            var payload = await response.Content.ReadFromJsonAsync<ImprontaRuntResponseBody>(JsonOptions, ct);

            // 200 OK con ok:false (no es un error HTTP): Kyverum no pudo generar la impronta por una
            // razón de negocio (ej. placa+documento no coinciden con el propietario activo del
            // vehículo en el RUNT) — distinto de una respuesta malformada. Verificado contra el
            // proveedor real: para el MISMO documento, enviar placa+documento cuando no corresponden
            // da ok:false de forma consistente (reproducible, no intermitente), mientras que enviar
            // solo vin+documento (sin placa) sí resuelve — confirma que es un rechazo de datos, no un
            // problema de disponibilidad del proveedor. Se mantiene no transitorio (no reintentable
            // automáticamente): reintentar con los mismos datos no cambia el resultado.
            if (payload is { Ok: false })
            {
                var providerMessage = string.IsNullOrWhiteSpace(payload.Message)
                    ? "Kyverum RUNT no pudo generar la impronta para los datos enviados."
                    : payload.Message;

                // El mensaje de negocio se loguea aquí porque es la única capa que lo ve: más arriba
                // solo se propaga el mensaje ya traducido de la excepción (ver ImprontaRuntException),
                // y antes de este log no quedaba ningún rastro diagnosticable del motivo real.
                ImprontaRuntLog.BusinessRejection(logger, providerMessage);
                throw new ImprontaRuntException($"Impronta no disponible: {providerMessage}", isTransient: false);
            }

            if (payload is null
                || string.IsNullOrWhiteSpace(payload.Pdf)
                || string.IsNullOrWhiteSpace(payload.Hash)
                || string.IsNullOrWhiteSpace(payload.Radicado)
                || string.IsNullOrWhiteSpace(payload.FechaImpresa))
                throw new ImprontaRuntException("Respuesta inválida de Kyverum RUNT.", isTransient: false);

            return new ImprontaExternalResult(payload.Pdf!, payload.Hash!, payload.Radicado!, payload.FechaImpresa!);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (TaskCanceledException)
        {
            throw new ImprontaRuntException("Timeout consultando Kyverum RUNT.", isTransient: true);
        }
        catch (HttpRequestException ex)
        {
            throw new ImprontaRuntException($"Error de red consultando Kyverum RUNT: {ex.Message}", isTransient: true);
        }
        catch (JsonException)
        {
            throw new ImprontaRuntException("No se pudo interpretar la respuesta de Kyverum RUNT.", isTransient: false);
        }
    }

    /// <summary>
    /// Mapea una respuesta de error de Kyverum RUNT a <see cref="ImprontaRuntException"/>. El código de
    /// error (cuerpo, no el status HTTP) determina el mensaje y la transitoriedad: <c>UPSTREAM_UNAVAILABLE</c>
    /// (o cualquier 5xx) es transitorio; <c>VALIDATION_ERROR</c>/<c>UNAUTHORIZED</c> (o desconocido) no lo es.
    /// </summary>
    private async Task<ImprontaRuntException> BuildErrorAsync(HttpResponseMessage response, CancellationToken ct)
    {
        var statusCode = (int)response.StatusCode;
        var errorBody = await SafeReadBodyAsync(response, ct);
        var envelope = TryParseError(errorBody);
        var code = envelope?.Error?.Code ?? "UNKNOWN";
        var providerMessage = envelope?.Error?.Message ?? "Error desconocido de Kyverum RUNT.";
        var transient = string.Equals(code, "UPSTREAM_UNAVAILABLE", StringComparison.Ordinal) || statusCode >= 500;

        // Nunca se loguea la API key ni el cuerpo crudo completo — solo código/mensaje del proveedor.
        ImprontaRuntLog.ProviderError(logger, statusCode, code, providerMessage);

        var exceptionMessage = code switch
        {
            "VALIDATION_ERROR" => BuildValidationMessage(providerMessage, envelope?.Error?.Details),
            // El 401 es ambiguo entre key inválida y scope insuficiente: Kyverum no lo distingue por
            // status code, así que el mensaje se arma inspeccionando error.message del cuerpo.
            "UNAUTHORIZED" => $"Kyverum RUNT rechazó la autenticación: {providerMessage}",
            "UPSTREAM_UNAVAILABLE" => $"Kyverum RUNT no disponible temporalmente: {providerMessage}",
            _ => $"Kyverum RUNT respondió error ({statusCode}): {providerMessage}",
        };

        return new ImprontaRuntException(exceptionMessage, transient);
    }

    private static string BuildValidationMessage(string message, IReadOnlyList<ImprontaRuntErrorDetail>? details)
    {
        if (details is not { Count: > 0 })
            return $"Datos de la impronta inválidos: {message}";

        var detailText = string.Join("; ", details.Select(d => $"{d.Field}: {d.Reason}"));
        return $"Datos de la impronta inválidos: {message} ({detailText})";
    }

    private static ImprontaRuntErrorEnvelope? TryParseError(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return null;

        try
        {
            return JsonSerializer.Deserialize<ImprontaRuntErrorEnvelope>(body, JsonOptions);
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

    // ── Contrato Kyverum RUNT (CONTRATO-API.md — POST /v1/improntas:generar) ──────────────────
    private sealed record ImprontaRuntRequestBody(
        [property: JsonPropertyName("placa"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Placa,
        [property: JsonPropertyName("documento")] string Documento,
        [property: JsonPropertyName("numMotor"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? NumMotor,
        [property: JsonPropertyName("numChasis"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? NumChasis,
        [property: JsonPropertyName("numSerie"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? NumSerie,
        [property: JsonPropertyName("marca"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Marca,
        [property: JsonPropertyName("linea"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Linea,
        [property: JsonPropertyName("modelo"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Modelo,
        [property: JsonPropertyName("orgNombre")] string OrgNombre,
        [property: JsonPropertyName("orgNit"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? OrgNit,
        [property: JsonPropertyName("orgCiudad"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? OrgCiudad,
        [property: JsonPropertyName("operador")] string Operador,
        [property: JsonPropertyName("vin"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Vin);

    private sealed record ImprontaRuntResponseBody(
        [property: JsonPropertyName("ok")] bool Ok,
        [property: JsonPropertyName("pdf")] string? Pdf,
        [property: JsonPropertyName("hash")] string? Hash,
        [property: JsonPropertyName("radicado")] string? Radicado,
        [property: JsonPropertyName("fechaImpresa")] string? FechaImpresa,
        // Presente cuando ok:false (200 con motivo de negocio, ej. "El RUNT no expone identificadores
        // para este vehículo") — no documentado en CONTRATO-API.md, descubierto contra el proveedor real.
        [property: JsonPropertyName("message")] string? Message = null);

    private sealed record ImprontaRuntErrorEnvelope(
        [property: JsonPropertyName("error")] ImprontaRuntErrorBody? Error);

    private sealed record ImprontaRuntErrorBody(
        [property: JsonPropertyName("code")] string? Code,
        [property: JsonPropertyName("message")] string? Message,
        [property: JsonPropertyName("traceId")] string? TraceId,
        [property: JsonPropertyName("details")] IReadOnlyList<ImprontaRuntErrorDetail>? Details);

    private sealed record ImprontaRuntErrorDetail(
        [property: JsonPropertyName("field")] string? Field,
        [property: JsonPropertyName("reason")] string? Reason);
}

/// <summary>Logging source-generated (CA1848) del cliente Kyverum RUNT. Nunca incluye la API key.</summary>
internal static partial class ImprontaRuntLog
{
    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Kyverum RUNT respondió error HTTP {StatusCode} código {ErrorCode}: {ErrorMessage}")]
    public static partial void ProviderError(ILogger logger, int statusCode, string errorCode, string errorMessage);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Kyverum RUNT respondió 200 con ok:false (rechazo de negocio de los datos enviados): {ProviderMessage}")]
    public static partial void BusinessRejection(ILogger logger, string providerMessage);
}
