using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Flit.Infrastructure.Consultations;
using Flit.Infrastructure.KyverumRunt;
using Flit.Tramites.Application.UseCases.RuntConfirmation;
using Flit.Tramites.Domain.RuntConfirmation;
using Microsoft.Extensions.Options;

namespace Flit.Infrastructure.RuntConfirmation;

/// <summary>
/// Consumidor PROPIO del RUNT para la Confirmación (HU #12309 AC3): elige el proveedor por la
/// <c>providerKey</c> de la configuración global y devuelve el JSON tal como llegó. No pasa por
/// <c>IConsultationProviderChainResolver</c> ni por el override por tenant, ni por los mappers del
/// wizard: al motor le sirve el crudo y solo el crudo.
///
/// Clasificación (la misma que los providers del wizard, sin el mock):
/// <list type="bullet">
/// <item>Kyverum: <c>ok:false</c> ⇒ NotFound (con el cuerpo); excepción transitoria o no ⇒ Error.</item>
/// <item>Verifik: 404 ⇒ NotFound (cuerpo si lo hay); 5xx/timeout/red/JSON ilegible ⇒ Error; 200 ⇒ Found.</item>
/// </list>
/// Nunca lanza al runner: un fallo del proveedor es un intento <c>error</c>, no una corrida caída.
/// </summary>
internal sealed class RuntVehicleRawClient(
    KyverumRuntApiClient kyverum,
    VerifikRuntRawHttpClient verifik) : IRuntVehicleRawClient
{
    public Task<RuntRawQueryResult> ConsultAsync(string providerKey, RuntRawQuery query, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        return providerKey switch
        {
            RuntConfirmationProviderKeys.Kyverum => ConsultKyverumAsync(query, ct),
            RuntConfirmationProviderKeys.Verifik => verifik.ConsultAsync(query, ct),
            _ => Task.FromResult(RuntRawQueryResult.Error($"Proveedor no soportado: {providerKey}")),
        };
    }

    private async Task<RuntRawQueryResult> ConsultKyverumAsync(RuntRawQuery query, CancellationToken ct)
    {
        var q = query.Vin is not null
            ? new KyverumRuntVehicleQuery(Vin: query.Vin, Placa: null, Documento: null, TipoDocumento: null)
            : new KyverumRuntVehicleQuery(
                Vin: null,
                Placa: query.Plate,
                Documento: query.Document!.Number,
                TipoDocumento: KyverumRuntDocType.Normalize(query.Document.Type));

        try
        {
            var (_, raw) = await kyverum.ConsultarVehiculoConCrudoAsync(q, ct).ConfigureAwait(false);
            return new RuntRawQueryResult(RuntRawOutcome.Found, raw, null);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (KyverumRuntException ex) when (ex.IsNotFound)
        {
            return new RuntRawQueryResult(
                RuntRawOutcome.NotFound,
                RuntVehicleSnapshotParser.NotFoundPayload(RuntConfirmationProviderKeys.Kyverum, null, ex.Message),
                ex.Message);
        }
        catch (KyverumRuntException ex)
        {
            return RuntRawQueryResult.Error(ex.Message);
        }
    }
}

/// <summary>
/// GET crudo a Verifik v2 (<c>vehicle-by-vin</c> / <c>vehicle-by-plate</c>), typed HttpClient con las
/// mismas <see cref="VerifikOptions"/> que el wizard. Un reintento ante fallo transitorio, porque la
/// primera llamada calienta el caché de Verifik (mismo motivo que <c>VerifikConsultationProvider</c>).
/// </summary>
internal sealed class VerifikRuntRawHttpClient(HttpClient http, IOptions<VerifikOptions> options)
{
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(1);
    private readonly VerifikOptions _options = options.Value;

    public async Task<RuntRawQueryResult> ConsultAsync(RuntRawQuery query, CancellationToken ct)
    {
        var url = query.Vin is not null
            ? $"/v2/co/runt/vehicle-by-vin?vin={Uri.EscapeDataString(query.Vin)}"
            : $"/v2/co/runt/vehicle-by-plate?plate={Uri.EscapeDataString(query.Plate!)}" +
              $"&documentType={Uri.EscapeDataString(query.Document!.Type)}" +
              $"&documentNumber={Uri.EscapeDataString(query.Document.Number)}";

        var (result, transient) = await SendOnceAsync(url, ct).ConfigureAwait(false);
        if (!transient)
            return result;

        await Task.Delay(RetryDelay, ct).ConfigureAwait(false);
        var (retry, _) = await SendOnceAsync(url, ct).ConfigureAwait(false);
        return retry;
    }

    private async Task<(RuntRawQueryResult Result, bool Transient)> SendOnceAsync(string url, CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            if (!string.IsNullOrWhiteSpace(_options.ApiToken))
                request.Headers.Authorization = new AuthenticationHeaderValue(_options.AuthScheme, _options.ApiToken);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            using var response = await http.SendAsync(request, ct).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                var raw = IsJsonObject(body) ? body : RuntVehicleSnapshotParser.NotFoundPayload(RuntConfirmationProviderKeys.Verifik, 404, "Vehículo no encontrado");
                // El cuerpo real de un 404 de Verifik no trae data.informacionGeneral: el parser lo
                // lee como ilegible. Se marca explícitamente como no encontrado.
                if (IsJsonObject(body))
                    raw = MarkNotFound(body);
                return (new RuntRawQueryResult(RuntRawOutcome.NotFound, raw, "Vehículo no encontrado"), false);
            }

            if (!response.IsSuccessStatusCode)
            {
                // El cuerpo del error se conserva como crudo del intento: sin él, un 409 de Verifik en el
                // historial no dice nada (¿parámetro faltante?, ¿petición duplicada?) y no se puede
                // diagnosticar sin repetir la consulta pagada.
                var status = (int)response.StatusCode;
                var raw = IsJsonObject(body) ? MarkError(body, status) : null;
                return (new RuntRawQueryResult(RuntRawOutcome.Error, raw, ErrorMessage(body, status)), status >= 500);
            }

            if (!IsJsonObject(body))
                return (RuntRawQueryResult.Error("Respuesta ilegible del proveedor"), false);

            return (new RuntRawQueryResult(RuntRawOutcome.Found, body, null), false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (TaskCanceledException)
        {
            return (RuntRawQueryResult.Error("Timeout del proveedor"), true);
        }
        catch (HttpRequestException ex)
        {
            return (RuntRawQueryResult.Error($"Error de red: {ex.Message}"), true);
        }
    }

    private static bool IsJsonObject(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return false;
        try
        {
            using var doc = JsonDocument.Parse(body);
            return doc.RootElement.ValueKind == JsonValueKind.Object;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>«HTTP 409 — MissingParameter: missing plate», con lo que Verifik haya puesto en <c>code</c>/<c>message</c>.</summary>
    private static string ErrorMessage(string? body, int status)
    {
        var prefix = $"HTTP {status}";
        if (!IsJsonObject(body))
            return prefix;
        using var doc = JsonDocument.Parse(body!);
        var code = doc.RootElement.TryGetProperty("code", out var c) && c.ValueKind == JsonValueKind.String ? c.GetString() : null;
        var message = doc.RootElement.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String ? m.GetString() : null;
        var detail = string.Join(": ", new[] { code, message }.Where(x => !string.IsNullOrWhiteSpace(x)));
        return detail.Length == 0 ? prefix : $"{prefix} — {detail}";
    }

    /// <summary>Envuelve el cuerpo de un error HTTP (4xx/5xx) para guardarlo como evidencia del intento.</summary>
    private static string MarkError(string body, int status) =>
        JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["ok"] = false,
            ["error"] = true,
            ["providerKey"] = RuntConfirmationProviderKeys.Verifik,
            ["statusCode"] = status,
            ["providerBody"] = JsonSerializer.Deserialize<JsonElement>(body),
        });

    /// <summary>Envuelve el cuerpo del 404 para conservarlo como evidencia y que el parser lo lea como no encontrado.</summary>
    private static string MarkNotFound(string body) =>
        JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["ok"] = false,
            ["notFound"] = true,
            ["providerKey"] = RuntConfirmationProviderKeys.Verifik,
            ["statusCode"] = 404,
            ["providerBody"] = JsonSerializer.Deserialize<JsonElement>(body),
        });
}
