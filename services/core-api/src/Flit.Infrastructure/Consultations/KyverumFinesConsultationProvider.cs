using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Flit.Tramites.Application.UseCases.Consultations;
using Microsoft.Extensions.Options;

namespace Flit.Infrastructure.Consultations;

/// <summary>
/// FEATURE 05 — proveedor KYVERUM de comparendos para PERSONA JURÍDICA (por NIT).
/// Se usa cuando la compañía tiene <c>fines_query_source = external</c> y el actor es jurídico;
/// las personas naturales siguen yendo por <c>verifik_simit</c>.
///
/// ⚠️ ARRANCA EN MODO MOCK (<c>KYVERUM_FINES_MODE</c>, default "mock") porque el proveedor no ha
/// entregado credenciales ni especificación: la URL y la ruta de
/// <see cref="KyverumFinesOptions"/> son provisionales. La demo funciona sin credenciales y
/// conectar el proveedor real es un cambio de appsettings, no de código.
///
/// RIESGO al activar el modo real sin credenciales válidas: un 401/403 se traduce a un check
/// "error", que es un bloqueo DURO no subsanable con "asumo el riesgo" — dejaría sin poder crear
/// trámites a TODO traspaso con comprador persona jurídica. No activar hasta verificar la
/// credencial contra el entorno del proveedor.
///
/// Reutiliza <see cref="FinesCheckFactory"/>: las claves de los checks deben ser idénticas a las
/// de los otros dos proveedores porque de ellas cuelga el gate de radicación al OT.
/// </summary>
internal sealed class KyverumFinesConsultationProvider(
    HttpClient http,
    IOptions<KyverumFinesOptions> options,
    IOptions<ConsultationProviderModeOptions> modeOptions) : IConsultationProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly KyverumFinesOptions _opts = options.Value;
    private readonly ConsultationProviderModeOptions _modes = modeOptions.Value;

    public string Key => Key_;

    public Task<ConsultationResult> ConsultAsync(ConsultationContext ctx, CancellationToken ct)
    {
        if (ConsultationProviderModeOptions.IsMock(_modes.KyverumFinesMode))
            return Task.FromResult(MockResult());

        return RealConsultAsync(ctx, ct);
    }

    private async Task<ConsultationResult> RealConsultAsync(ConsultationContext ctx, CancellationToken ct)
    {
        var documentType = GetValue(ctx, "owner_document_type") ?? GetValue(ctx, "documentType");
        var documentNumber = GetValue(ctx, "owner_document_number") ?? GetValue(ctx, "documentNumber");

        if (string.IsNullOrWhiteSpace(documentType) || string.IsNullOrWhiteSpace(documentNumber))
            return InputError("Se requiere documento (tipo+número) para consultar comparendos");

        var url = $"{_opts.InfractionPath}" +
                  $"?documentType={Uri.EscapeDataString(documentType)}" +
                  $"&documentNumber={Uri.EscapeDataString(documentNumber)}";

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            // Sin ApiKey configurada no se envía la cabecera (mismo guard que los demás proveedores):
            // evita mandar "Bearer " vacío, que el proveedor rechazaría con un 401 confuso.
            if (!string.IsNullOrWhiteSpace(_opts.ApiKey))
                request.Headers.Authorization = new AuthenticationHeaderValue(_opts.AuthScheme, _opts.ApiKey);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            using var response = await http.SendAsync(request, ct);

            if (response.StatusCode == HttpStatusCode.NotFound)
                return NotFound();

            if (!response.IsSuccessStatusCode)
                return ProviderUnavailable();

            // Se asume la forma del SIMIT (la fuente última es la misma). Si el proveedor entrega
            // otra forma, el ajuste vive en este DTO + su mapper, sin tocar el resto del flujo.
            var payload = await response.Content.ReadFromJsonAsync<FlitFinesResponse>(JsonOptions, ct);
            if (payload is null)
                return ProviderUnavailable();

            var mapped = FlitFinesResultMapper.Map(payload);
            return new ConsultationResult(Key_, mapped.Overall, RebrandChecks(mapped.Checks), mapped.HydratedFields);
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

    // El mapper compartido etiqueta el Source con su propio proveedor: se reescribe al de esta
    // clase para que la trazabilidad del check apunte a quien realmente respondió.
    private static IReadOnlyList<ConsultationCheck> RebrandChecks(IReadOnlyList<ConsultationCheck> checks) =>
        [.. checks.Select(c => c with { Source = Key_ })];

    // Caso limpio: sin multas ni acuerdos → green, con las claves del contrato compartido.
    private static ConsultationResult MockResult()
    {
        var mapped = FlitFinesResultMapper.Map(new FlitFinesResponse { Multas = [], AcuerdosPago = [] });
        return new ConsultationResult(Key_, mapped.Overall, RebrandChecks(mapped.Checks), mapped.HydratedFields);
    }

    private static ConsultationResult NotFound() =>
        new(Key_, "green",
            [new ConsultationCheck(FinesCheckFactory.KeyMultas, FinesCheckFactory.LabelMultas, "ok", Key_,
                "Sin registros SIMIT")],
            []);

    private static ConsultationResult ProviderUnavailable() =>
        new(Key_, "red",
            [new ConsultationCheck("provider", "Consulta de comparendos", "error", Key_,
                "No fue posible verificar la información de comparendos en este momento. Vuelve a intentarlo en unos minutos.")],
            []);

    private static ConsultationResult InputError(string message) =>
        new(Key_, "yellow",
            [new ConsultationCheck("input", "Datos de consulta", "unknown", Key_, message)],
            []);

    private static string? GetValue(ConsultationContext ctx, string key) =>
        ctx.FieldValues.TryGetValue(key, out var v) ? v : null;

    private const string Key_ = "kyverum_fines";
}
