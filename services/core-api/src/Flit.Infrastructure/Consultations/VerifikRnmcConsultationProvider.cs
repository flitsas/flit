using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Flit.Tramites.Application.UseCases.Consultations;
using Microsoft.Extensions.Options;

namespace Flit.Infrastructure.Consultations;

/// <summary>
/// Proveedor Verifik RNMC (§3.1). Verifica medidas correctivas del titular por documento.
/// Requiere: documentType (CC/CE), documentNumber, document_issue_date (DD/MM/YYYY).
/// Mode=mock devuelve persona sin medidas → swap transparente.
/// </summary>
internal sealed class VerifikRnmcConsultationProvider(
    HttpClient http,
    IOptions<VerifikOptions> verifikOptions,
    IOptions<ConsultationProviderModeOptions> modeOptions) : IConsultationProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly VerifikOptions _opts = verifikOptions.Value;
    private readonly ConsultationProviderModeOptions _modes = modeOptions.Value;

    public string Key => "verifik_rnmc";

    public Task<ConsultationResult> ConsultAsync(ConsultationContext ctx, CancellationToken ct)
    {
        if (ConsultationProviderModeOptions.IsMock(_modes.VerifikRnmcMode))
            return Task.FromResult(MockResult());

        return RealConsultAsync(ctx, ct);
    }

    private async Task<ConsultationResult> RealConsultAsync(ConsultationContext ctx, CancellationToken ct)
    {
        var documentType = GetValue(ctx, "owner_document_type") ?? GetValue(ctx, "documentType");
        var documentNumber = GetValue(ctx, "owner_document_number") ?? GetValue(ctx, "documentNumber");
        // La fecha de expedición es obligatoria para RNMC. Campo: document_issue_date.
        var issueDate = GetValue(ctx, "document_issue_date") ?? GetValue(ctx, "documentIssueDate");

        if (string.IsNullOrWhiteSpace(documentType) ||
            string.IsNullOrWhiteSpace(documentNumber) ||
            string.IsNullOrWhiteSpace(issueDate))
        {
            return InputError("Se requiere documentType, documentNumber y document_issue_date (DD/MM/YYYY) para RNMC");
        }

        var url = $"/v2/co/policia/rnmc?documentType={Uri.EscapeDataString(documentType)}" +
                  $"&documentNumber={Uri.EscapeDataString(documentNumber)}" +
                  $"&date={Uri.EscapeDataString(issueDate)}";

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

            var payload = await response.Content.ReadFromJsonAsync<VerifikRnmcResponse>(JsonOptions, ct);
            if (payload is null)
                return ProviderUnavailable();

            return VerifikRnmcResultMapper.Map(payload);
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
        // JSON canónico del doc §3.1 — persona sin medidas correctivas (caso limpio para DEV).
        var mock = new VerifikRnmcResponse
        {
            Data = new VerifikRnmcData
            {
                CorrectiveMeasures = [],
                Records = [],
                ArrayName = ["TITULAR", "MOCK"],
                DocumentType = "CC",
                DocumentNumber = "00000000",
                FirstName = "TITULAR",
                LastName = "MOCK",
                FullName = "TITULAR MOCK",
            },
        };
        return VerifikRnmcResultMapper.Map(mock);
    }

    private static ConsultationResult NotFound() =>
        new(Key_, "green",
            [new ConsultationCheck("medidas_correctivas", "Medidas correctivas (Policía)", "ok", Key_, "Sin registros RNMC")],
            []);

    // No se pudo verificar RNMC (no-200/timeout/red/respuesta ilegible). Dato crítico:
    // check "error" (bloqueo DURO, no subsanable) con mensaje amigable sin exponer el proveedor.
    private static ConsultationResult ProviderUnavailable() =>
        new(Key_, "red",
            [new ConsultationCheck("provider", "Consulta RNMC (Policía)", "error", Key_,
                "No fue posible verificar la información en el RNMC en este momento. Vuelve a intentarlo en unos minutos.")],
            []);

    private static ConsultationResult InputError(string message) =>
        new(Key_, "yellow",
            [new ConsultationCheck("input", "Datos de consulta", "unknown", Key_, message)],
            []);

    private static string? GetValue(ConsultationContext ctx, string key) =>
        ctx.FieldValues.TryGetValue(key, out var v) ? v : null;

    private const string Key_ = "verifik_rnmc";
}
