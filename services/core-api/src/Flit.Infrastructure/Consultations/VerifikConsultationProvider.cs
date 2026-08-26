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
    IOptions<VerifikOptions> options,
    IOptions<ConsultationProviderModeOptions> modeOptions) : IConsultationProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    // Respiro antes del reintento: la primera llamada (aunque falle por timeout/504)
    // CALIENTA el caché de Verifik, que en frío proxya el RUNT en vivo. Un breve
    // descanso le da margen a poblarlo antes del segundo intento.
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(1);

    private readonly VerifikOptions _options = options.Value;
    private readonly ConsultationProviderModeOptions _modes = modeOptions.Value;

    public string Key => "verifik";

    public async Task<ConsultationResult> ConsultAsync(ConsultationContext ctx, CancellationToken ct)
    {
        var vin = GetValue(ctx, "vin");
        var plate = GetValue(ctx, "plate");

        // Toggle VERIFIK_VEHICLE_MODE=mock (Consultations:VerifikVehicleMode): devuelve un vehículo
        // canónico VERDE reusando el VIN/placa capturado, para desatascar el pre-vuelo cuando el RUNT
        // (Verifik) esté caído o el token vencido, sin depender de la red. Mismo patrón que el SIMIT.
        if (ConsultationProviderModeOptions.IsMock(_modes.VerifikVehicleMode))
            return MockResult(vin, plate);

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
                return (ProviderUnavailable(), transient);
            }

            var payload = await response.Content.ReadFromJsonAsync<VerifikVehicleResponse>(JsonOptions, ct);
            if (payload is null)
                return (ProviderUnavailable(), false);

            return (VerifikResultMapper.MapVehicle(payload), false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (TaskCanceledException)
        {
            // Timeout del HttpClient (no cancelación del caller): transitorio.
            return (ProviderUnavailable(), true);
        }
        catch (HttpRequestException)
        {
            // Error de red (DNS/conexión/reset): transitorio.
            return (ProviderUnavailable(), true);
        }
        catch (JsonException)
        {
            return (ProviderUnavailable(), false);
        }
    }

    // Vehículo VERDE con datos REALES del RUNT ya obtenidos para el VIN LRWYGCEKXTC564524
    // (Tesla Model Y, placa QPL705) que quedaron en la BD de una consulta previa. Se reusa el
    // VIN/placa capturados en el trámite (fallback a los del registro conocido) y se pasa por el
    // mapper real para hidratar los mismos campos que la respuesta viva. Desatasca el pre-vuelo
    // para probar la validación de identidad mientras el RUNT/Verifik está indisponible. Sin red.
    private static ConsultationResult MockResult(string? vin, string? plate)
    {
        var effectiveVin = string.IsNullOrWhiteSpace(vin) ? "LRWYGCEKXTC564524" : vin;
        // Sentinels de PRUEBA (solo modo mock), por prefijo de placa. El registro real es un vehículo
        // verde y sin novedades, así que sin ellos los bloqueos duros de la familia OTROS no son
        // alcanzables en local: no hay forma de que el mock diga «este vehículo no tiene carrocería»
        // ni «el RUNT afirma que no tiene gravamen». Cada uno mueve UN dato del registro canónico.
        //
        //   SNV… → SOAT no vigente          (novedad de fuentes externas, p. ej. desde ICT)
        //   SCA… → sin carrocería           (bloquea CAMBIO_CARROCERIA)
        //   SGR… → RUNT afirma sin gravamen (bloquea LEVANTAMIENTO_PRENDA)
        //   CPR… → RUNT reporta prenda      (levantamiento válido, y precarga del numeral 20)
        var prefijo = plate?.Trim() ?? string.Empty;
        bool Sentinel(string codigo) => prefijo.StartsWith(codigo, StringComparison.OrdinalIgnoreCase);

        var soatVencido = Sentinel("SNV");
        var sinCarroceria = Sentinel("SCA");
        var sinGravamen = Sentinel("SGR");
        var conPrenda = Sentinel("CPR");
        return VerifikResultMapper.MapVehicle(new VerifikVehicleResponse
        {
            Data = new VerifikVehicleData
            {
                InformacionGeneral = new VerifikInformacionGeneral
                {
                    NoVin = effectiveVin,
                    NoChasis = effectiveVin,
                    NoPlaca = string.IsNullOrWhiteSpace(plate) ? "QPL705" : plate,
                    Marca = "TESLA",
                    Linea = "MODELO Y",
                    Modelo = "2026",
                    Color = "PLATA METALICO",
                    ClaseVehiculo = "CAMIONETA",
                    TipoCarroceria = sinCarroceria ? "SIN CARROCERIA" : "SUV",
                    TipoServicio = "Particular",
                    TipoCombustible = "ELECTRICO",
                    Cilindraje = "0",
                    PasajerosSentados = "5",
                    NoEjes = "2",
                    PesoBruto = "1992",
                    EstadoDelVehiculo = "ACTIVO",
                    // El registro real no trajo señal de gravámenes/prendas ni RTM: sin sentinel se
                    // dejan sin dato (check 'unknown', que no bloquea porque «no sé» no es «no tiene»).
                    // SOAT vigente + estado ACTIVO → overall green.
                    TieneGravamenes = sinGravamen ? "NO" : conPrenda ? "SI" : null,
                    Prendas = sinGravamen ? "NO" : conPrenda ? "SI" : null,
                    FechaMatricula = "06/05/2026",
                },
                Soat =
                [
                    new VerifikSoat
                    {
                        Estado = soatVencido ? "VENCIDO" : "VIGENTE",
                        FechaVencimiento = soatVencido ? "01/01/2020" : "05/05/2027",
                        EntidadExpideSoat = "LA PREVISORA S.A.COMPAÑIA DE SEGUROS",
                    },
                ],
                TecnoMecanica = [],
                GarantiasMobiliarias = [],
            },
        });
    }

    private static ConsultationResult VehicleNotFound() =>
        new(
            "verifik",
            "red",
            [
                new ConsultationCheck("vehiculo", "Vehículo RUNT", "fail", "verifik", "Vehículo no encontrado en RUNT")
            ],
            []);

    // No se pudo verificar el vehículo en el RUNT (no-200/timeout/red/respuesta ilegible).
    // El dato es crítico para el trámite: se emite un check "error" (bloqueo DURO, overall red,
    // no subsanable con "aceptar riesgo"), con un mensaje amigable que NO expone el proveedor
    // ni el código HTTP crudo (el panel ya rotula la fuente como RUNT).
    private static ConsultationResult ProviderUnavailable() =>
        new(
            "verifik",
            "red",
            [
                new ConsultationCheck("provider", "Consulta de vehículo", "error", "verifik", ProviderUnavailableMessage)
            ],
            []);

    private const string ProviderUnavailableMessage =
        "No fue posible verificar la información del vehículo en el RUNT en este momento. Vuelve a intentarlo en unos minutos.";

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
