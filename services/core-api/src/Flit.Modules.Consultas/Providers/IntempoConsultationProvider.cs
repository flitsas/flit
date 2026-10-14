using System.Net.Http.Json;
using System.Text.Json;
using Flit.Tramites.Application.UseCases.Consultations;
using Microsoft.Extensions.Options;

namespace Flit.Infrastructure.Consultations;

/// <summary>
/// Proveedor INTEMPO vehículo (§4.1 VIN / §4.2 PLACA). Un único endpoint POST;
/// el body varía según tipoConsulta. Sin auth: acceso por IP/contrato.
/// Cuando tipoConsulta=VIN el campo noPlaca contiene el VIN (quirk del schema compartido).
/// VIN se prioriza sobre placa si ambos están disponibles.
/// Mode=mock devuelve vehículo Tesla Model 3 limpio del ejemplo del doc → swap transparente.
/// </summary>
internal sealed class IntempoConsultationProvider(
    HttpClient http,
    IOptions<ConsultationProviderModeOptions> modeOptions) : IConsultationProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ConsultationProviderModeOptions _modes = modeOptions.Value;

    public string Key => "intempo";

    public Task<ConsultationResult> ConsultAsync(ConsultationContext ctx, CancellationToken ct)
    {
        if (ConsultationProviderModeOptions.IsMock(_modes.IntempoMode))
            return Task.FromResult(MockResult());

        return RealConsultAsync(ctx, ct);
    }

    private async Task<ConsultationResult> RealConsultAsync(ConsultationContext ctx, CancellationToken ct)
    {
        var vin = GetValue(ctx, "vin");
        var plate = GetValue(ctx, "plate");

        object body;

        if (IsValidVin(vin))
            body = new { tipoConsulta = "VIN", noPlaca = vin };
        else if (!string.IsNullOrWhiteSpace(plate))
            body = new { tipoConsulta = "PLACA", noPlaca = plate };
        else
            return InputError("Se requiere VIN (17 chars) o placa para consultar INTEMPO");

        return await SendAsync(body, ct);
    }

    private async Task<ConsultationResult> SendAsync(object body, CancellationToken ct)
    {
        try
        {
            const string path = "/consultaAseguradora/consulta/vehiculos";
            using var response = await http.PostAsJsonAsync(path, body, JsonOptions, ct);

            if (!response.IsSuccessStatusCode)
                return ProviderUnavailable();

            var payload = await response.Content.ReadFromJsonAsync<IntempoVehicleResponse>(JsonOptions, ct);
            if (payload is null)
                return ProviderUnavailable();

            return IntempoVehicleResultMapper.Map(payload);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (TaskCanceledException)
        {
            return ProviderUnavailable();
        }
        catch (HttpRequestException)
        {
            return ProviderUnavailable();
        }
        catch (JsonException)
        {
            return ProviderUnavailable();
        }
    }

    // Mock con forma idéntica al ejemplo del doc §4.1 (Tesla Model 3 — vehículo limpio).
    private static ConsultationResult MockResult()
    {
        var mock = new IntempoVehicleResponse
        {
            NoRegistro = "613001563",
            NoLicenciaTransito = "10037959398",
            EstadoDelVehiculo = "ACTIVO",
            NoPlaca = "QMW383",
            NoVin = "LRW3E7FS7TC753038",
            NoChasis = "LRW3E7FS7TC753038",
            Marca = "TESLA",
            Linea = "MODEL 3",
            Modelo = "2026",
            Color = "BLANCO PERLA MULTICAPA",
            Cilindraje = "0",
            TieneGravamenes = "NO",
            Prendas = "NO",
            TipoServicio = "Particular",
            ClaseVehiculo = "AUTOMOVIL",
            TipoCarroceria = "SEDAN",
            TipoCombustible = "ELECTRICO",
            FechaMatricula = "23/02/2026",
            OrganismoTransito = "STRIA DE TTOyTTE MEDELLIN",
            EsRegrabadoMotor = "NO",
            EsRegrabadoChasis = "NO",
            EsRegrabadoVin = "NO",
            Repotenciado = "NO",
            SoatNacionales =
            [
                new IntempoSoat
                {
                    NoPoliza = "3308006389310000",
                    FechaExpedicion = "19/02/2026",
                    FechaVigencia = "20/02/2026",
                    FechaVencimiento = "19/02/2027",
                    NitEntidad = "860002400",
                    EntidadExpideSoat = "LA PREVISORA S.A.COMPAÑIA DE SEGUROS",
                    Estado = "VIGENTE",
                },
            ],
            Gravamenes = [],
            LimitacionesPropiedad = [],
            CodigoResultado = "Exitoso",
            CodigoUUID = "00000000-0000-0000-0000-000000000000",
        };
        return IntempoVehicleResultMapper.Map(mock);
    }

    // No se pudo verificar el vehículo (no-200/timeout/red/respuesta ilegible). Dato crítico:
    // check "error" (bloqueo DURO, no subsanable) con mensaje amigable sin exponer el proveedor.
    private static ConsultationResult ProviderUnavailable() =>
        new("intempo", "red",
            [new ConsultationCheck("provider", "Consulta de vehículo", "error", "intempo",
                "No fue posible verificar la información del vehículo en el RUNT en este momento. Vuelve a intentarlo en unos minutos.")],
            []);

    private static ConsultationResult InputError(string message) =>
        new("intempo", "yellow",
            [new ConsultationCheck("input", "Datos de consulta", "unknown", "intempo", message)],
            []);

    private static string? GetValue(ConsultationContext ctx, string key) =>
        ctx.FieldValues.TryGetValue(key, out var v) ? v : null;

    private static bool IsValidVin(string? vin) =>
        !string.IsNullOrWhiteSpace(vin) && vin.Length == 17 && vin.All(char.IsLetterOrDigit);
}
