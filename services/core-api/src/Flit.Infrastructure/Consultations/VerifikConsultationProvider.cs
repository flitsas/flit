using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Flit.Tramites.Application.UseCases.Consultations;
using Microsoft.Extensions.Options;

namespace Flit.Infrastructure.Consultations;

/// <summary>
/// Proveedor Verifik RUNT. Consulta vehículo por VIN (prioritario) o por placa.
/// NUNCA lanza al handler: errores de red/HTTP se mapean a un <see cref="ConsultationResult"/>
/// válido con checks unknown/fail y overall yellow/red.
/// </summary>
internal sealed class VerifikConsultationProvider(
    HttpClient http,
    IOptions<VerifikOptions> options) : IConsultationProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    // Respiro antes del reintento: la primera llamada (aunque falle por timeout/504)
    // CALIENTA el caché de Verifik, que en frío proxya el RUNT en vivo. Un breve
    // descanso le da margen a poblarlo antes del segundo intento.
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(1);

    private readonly VerifikOptions _options = options.Value;

    public string Key => "verifik";

    public async Task<ConsultationResult> ConsultAsync(ConsultationContext ctx, CancellationToken ct)
    {
        var vin = GetValue(ctx, "vin");
        var plate = GetValue(ctx, "plate");

        if (IsValidVin(vin))
            return await ConsultByVinAsync(vin!, ct);

        if (!string.IsNullOrWhiteSpace(plate))
        {
            var documentType = GetValue(ctx, "owner_document_type") ?? GetValue(ctx, "documentType");
            var documentNumber = GetValue(ctx, "owner_document_number") ?? GetValue(ctx, "documentNumber");

            if (!string.IsNullOrWhiteSpace(documentType) && !string.IsNullOrWhiteSpace(documentNumber))
                return await ConsultByPlateAsync(plate!, documentType!, documentNumber!, ct);

            // MVP: consulta por placa requiere documento del propietario.
            return RequiresOwnerDocument();
        }

        return new ConsultationResult(
            "verifik",
            "yellow",
            [
                new ConsultationCheck(
                    "input",
                    "Datos de consulta",
                    "unknown",
                    "verifik",
                    "Se requiere VIN o placa para consultar el vehículo")
            ],
            []);
    }

    private Task<ConsultationResult> ConsultByVinAsync(string vin, CancellationToken ct)
    {
        var url = $"/v2/co/runt/vehicle-by-vin?vin={Uri.EscapeDataString(vin)}";
        return SendAsync(url, ct);
    }

    private Task<ConsultationResult> ConsultByPlateAsync(string plate, string documentType, string documentNumber, CancellationToken ct)
    {
        var url = $"/v2/co/runt/vehicle-by-plate?plate={Uri.EscapeDataString(plate)}" +
                  $"&documentType={Uri.EscapeDataString(documentType)}" +
                  $"&documentNumber={Uri.EscapeDataString(documentNumber)}";
        return SendAsync(url, ct);
    }

    /// <summary>
    /// Envía la consulta con un reintento único ante fallos TRANSITORIOS (timeout del
    /// HttpClient, 5xx del gateway de Verifik, error de red). El RUNT en frío puede
    /// tardar &gt;60s o devolver 504; pero la primera llamada deja el dato cacheado en
    /// Verifik, así que el segundo intento suele resolver en 2–5s. Los fallos
    /// DEFINITIVOS (404 vehículo no encontrado, auth, JSON inválido) NO se reintentan.
    /// </summary>
    private async Task<ConsultationResult> SendAsync(string url, CancellationToken ct)
    {
        var (result, transient) = await SendOnceAsync(url, ct);
        if (!transient)
            return result;

        await Task.Delay(RetryDelay, ct);

        var (retryResult, _) = await SendOnceAsync(url, ct);
        return retryResult;
    }

    /// <summary>
    /// Un intento de consulta. Devuelve el resultado y si el fallo fue transitorio
    /// (merece reintento). Nunca lanza salvo cancelación del caller.
    /// </summary>
    private async Task<(ConsultationResult Result, bool Transient)> SendOnceAsync(string url, CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            if (!string.IsNullOrWhiteSpace(_options.ApiToken))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue(_options.AuthScheme, _options.ApiToken);
            }
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            using var response = await http.SendAsync(request, ct);

            if (response.StatusCode == HttpStatusCode.NotFound)
                return (VehicleNotFound(), false);

            if (!response.IsSuccessStatusCode)
            {
                // 5xx (502/503/504) = el gateway de Verifik no obtuvo respuesta del RUNT:
                // transitorio, vale la pena reintentar. 4xx (auth/bad request) es definitivo.
                var transient = (int)response.StatusCode >= 500;
                return (ProviderUnavailable($"Verifik respondió {(int)response.StatusCode}"), transient);
            }

            var payload = await response.Content.ReadFromJsonAsync<VerifikVehicleResponse>(JsonOptions, ct);
            if (payload is null)
                return (ProviderUnavailable("Respuesta vacía de Verifik"), false);

            return (VerifikResultMapper.MapVehicle(payload), false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (TaskCanceledException)
        {
            // Timeout del HttpClient (no cancelación del caller): transitorio.
            return (ProviderUnavailable("Timeout consultando Verifik"), true);
        }
        catch (HttpRequestException ex)
        {
            // Error de red (DNS/conexión/reset): transitorio.
            return (ProviderUnavailable($"Error de red consultando Verifik: {ex.Message}"), true);
        }
        catch (JsonException)
        {
            return (ProviderUnavailable("No se pudo interpretar la respuesta de Verifik"), false);
        }
    }

    private static ConsultationResult VehicleNotFound() =>
        new(
            "verifik",
            "red",
            [
                new ConsultationCheck("vehiculo", "Vehículo RUNT", "fail", "verifik", "Vehículo no encontrado en RUNT")
            ],
            []);

    private static ConsultationResult ProviderUnavailable(string message) =>
        new(
            "verifik",
            "yellow",
            [
                new ConsultationCheck("provider", "Proveedor Verifik", "unknown", "verifik", message)
            ],
            []);

    private static ConsultationResult RequiresOwnerDocument() =>
        new(
            "verifik",
            "yellow",
            [
                new ConsultationCheck(
                    "input",
                    "Datos de consulta",
                    "unknown",
                    "verifik",
                    "Se requiere documento del propietario para consulta por placa")
            ],
            []);

    private static string? GetValue(ConsultationContext ctx, string key) =>
        ctx.FieldValues.TryGetValue(key, out var value) ? value : null;

    private static bool IsValidVin(string? vin) =>
        !string.IsNullOrWhiteSpace(vin) &&
        vin.Length == 17 &&
        vin.All(char.IsLetterOrDigit);
}
