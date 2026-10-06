using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Flit.Tramites.Application.UseCases.Consultations;
using Microsoft.Extensions.Options;

namespace Flit.Infrastructure.Consultations;

/// <summary>
/// FEATURE 05 — proveedor de comparendos de la fuente INTERNA (API de registro de FLIT).
/// Se usa cuando la compañía tiene <c>fines_query_source = internal</c>, tanto para personas
/// naturales como jurídicas (la fuente manda; el tipo de persona solo elige el proveedor externo).
///
/// Consulta por documento (tipo + número). Sin cabecera de autorización: el API no la exige.
/// Mode=mock devuelve la misma forma del contrato → swap transparente.
///
/// Contrato verificado contra la respuesta real (HTTP 200): mismo payload del SIMIT que Verifik
/// envuelve en value.value.data, pero plano y con los números como texto (los resuelve
/// JsonSerializerDefaults.Web vía AllowReadingFromString). Ver <see cref="FlitFinesResponse"/>.
/// </summary>
internal sealed class FlitFinesConsultationProvider(
    HttpClient http,
    IOptions<FlitRegistrationApiOptions> apiOptions,
    IOptions<ConsultationProviderModeOptions> modeOptions) : IConsultationProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly FlitRegistrationApiOptions _opts = apiOptions.Value;
    private readonly ConsultationProviderModeOptions _modes = modeOptions.Value;

    public string Key => Key_;

    public Task<ConsultationResult> ConsultAsync(ConsultationContext ctx, CancellationToken ct)
    {
        if (ConsultationProviderModeOptions.IsMock(_modes.FlitFinesMode))
            return Task.FromResult(MockResult());

        return RealConsultAsync(ctx, ct);
    }

    private async Task<ConsultationResult> RealConsultAsync(ConsultationContext ctx, CancellationToken ct)
    {
        var documentType = GetValue(ctx, "owner_document_type") ?? GetValue(ctx, "documentType");
        var documentNumber = GetValue(ctx, "owner_document_number") ?? GetValue(ctx, "documentNumber");

        if (string.IsNullOrWhiteSpace(documentType) || string.IsNullOrWhiteSpace(documentNumber))
            return InputError("Se requiere documento (tipo+número) para consultar comparendos");

        // Ruta relativa sin barra inicial: el BaseAddress conserva el segmento de stage (/pdn).
        var url = $"{_opts.NormalizedInfractionPath}" +
                  $"?documentType={Uri.EscapeDataString(documentType)}" +
                  $"&documentNumber={Uri.EscapeDataString(documentNumber)}";

        return await SendAsync(url, ct);
    }

    private async Task<ConsultationResult> SendAsync(string url, CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            using var response = await http.SendAsync(request, ct);

            if (response.StatusCode == HttpStatusCode.NotFound)
                return NotFound();

            if (!response.IsSuccessStatusCode)
                return ProviderUnavailable();

            var payload = await response.Content.ReadFromJsonAsync<FlitFinesResponse>(JsonOptions, ct);
            if (payload is null)
                return ProviderUnavailable();

            return FlitFinesResultMapper.Map(payload);
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

    // Caso limpio para DEV: sin multas ni acuerdos → green, con las mismas claves del contrato.
    private static ConsultationResult MockResult() =>
        FlitFinesResultMapper.Map(new FlitFinesResponse { Multas = [], AcuerdosPago = [] });

    private static ConsultationResult NotFound() =>
        new(Key_, "green",
            [new ConsultationCheck(FinesCheckFactory.KeyMultas, FinesCheckFactory.LabelMultas, "ok", Key_,
                "Sin registros SIMIT")],
            []);

    // No se pudo verificar (no-200/timeout/red/respuesta ilegible). Dato crítico: check "error"
    // (bloqueo DURO, no subsanable) con mensaje amigable que no expone el proveedor.
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

    private const string Key_ = "flit_fines";
}
