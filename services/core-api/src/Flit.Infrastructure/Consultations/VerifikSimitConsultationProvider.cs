using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Flit.Tramites.Application.UseCases.Consultations;
using Microsoft.Extensions.Options;

namespace Flit.Infrastructure.Consultations;

/// <summary>
/// Proveedor Verifik SIMIT (§3.5 por documento / §3.6 por placa).
/// Despacha por documento si hay documentType+documentNumber en contexto,
/// por placa si hay plate, priorizando documento (cubre al conductor).
/// Mode=mock devuelve JSON canónico con la misma forma del contrato → swap transparente.
/// </summary>
internal sealed class VerifikSimitConsultationProvider(
    HttpClient http,
    IOptions<VerifikOptions> verifikOptions,
    IOptions<ConsultationProviderModeOptions> modeOptions) : IConsultationProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly VerifikOptions _opts = verifikOptions.Value;
    private readonly ConsultationProviderModeOptions _modes = modeOptions.Value;

    public string Key => "verifik_simit";

    public Task<ConsultationResult> ConsultAsync(ConsultationContext ctx, CancellationToken ct)
    {
        if (ConsultationProviderModeOptions.IsMock(_modes.VerifikSimitMode))
            return Task.FromResult(MockResult());

        return RealConsultAsync(ctx, ct);
    }

    private async Task<ConsultationResult> RealConsultAsync(ConsultationContext ctx, CancellationToken ct)
    {
        var documentType = GetValue(ctx, "owner_document_type") ?? GetValue(ctx, "documentType");
        var documentNumber = GetValue(ctx, "owner_document_number") ?? GetValue(ctx, "documentNumber");
        var plate = GetValue(ctx, "plate");

        string url;

        if (!string.IsNullOrWhiteSpace(documentType) && !string.IsNullOrWhiteSpace(documentNumber))
        {
            url = $"/v2/co/simit/consultar?documentType={Uri.EscapeDataString(documentType)}" +
                  $"&documentNumber={Uri.EscapeDataString(documentNumber)}";
        }
        else if (!string.IsNullOrWhiteSpace(plate))
        {
            url = $"/v2/co/simit/consultar/placa?plate={Uri.EscapeDataString(plate)}";
        }
        else
        {
            return InputError("Se requiere documento (tipo+número) o placa para consultar SIMIT");
        }

        return await SendAsync(url, ct);
    }

    private async Task<ConsultationResult> SendAsync(string url, CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            if (!string.IsNullOrWhiteSpace(_opts.ApiToken))
                request.Headers.Authorization = new AuthenticationHeaderValue(_opts.AuthScheme, _opts.ApiToken);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            using var response = await http.SendAsync(request, ct);

            if (response.StatusCode == HttpStatusCode.NotFound)
                return NotFound();

            if (!response.IsSuccessStatusCode)
                return ProviderUnavailable();

            var payload = await response.Content.ReadFromJsonAsync<VerifikSimitResponse>(JsonOptions, ct);
            if (payload is null)
                return ProviderUnavailable();

            return VerifikSimitResultMapper.Map(payload);
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

    private static ConsultationResult MockResult()
    {
        // JSON canónico del doc §3.5 — sin multas pendientes (caso limpio para DEV).
        var mock = new VerifikSimitResponse
        {
            Value = new VerifikSimitValueOuter
            {
                Value = new VerifikSimitValueInner
                {
                    Data = new VerifikSimitData
                    {
                        Multas = [],
                        Cursos = [],
                        AcuerdosPago = [],
                        TotalMultasPagar = 0,
                        CantMultasPagar = 0,
                        TotalAcuerdosPagar = 0,
                        CantAcuerdosPagar = 0,
                    },
                },
            },
        };
        return VerifikSimitResultMapper.Map(mock);
    }

    private static ConsultationResult NotFound() =>
        new(Key_, "green",
            [new ConsultationCheck("multas", "Multas SIMIT", "ok", Key_, "Sin registros SIMIT")],
            []);

    // No se pudo verificar SIMIT (no-200/timeout/red/respuesta ilegible). Dato crítico:
    // check "error" (bloqueo DURO, no subsanable) con mensaje amigable sin exponer el proveedor.
    private static ConsultationResult ProviderUnavailable() =>
        new(Key_, "red",
            [new ConsultationCheck("provider", "Consulta SIMIT", "error", Key_,
                "No fue posible verificar la información en SIMIT en este momento. Vuelve a intentarlo en unos minutos.")],
            []);

    private static ConsultationResult InputError(string message) =>
        new(Key_, "yellow",
            [new ConsultationCheck("input", "Datos de consulta", "unknown", Key_, message)],
            []);

    private static string? GetValue(ConsultationContext ctx, string key) =>
        ctx.FieldValues.TryGetValue(key, out var v) ? v : null;

    private const string Key_ = "verifik_simit";
}
