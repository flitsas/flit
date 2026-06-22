using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Flit.Tramites.Application.UseCases.Consultations;
using Microsoft.Extensions.Options;

namespace Flit.Infrastructure.Consultations;

/// <summary>
/// Proveedor Verifik CONDUCTOR (RUNT). Único endpoint de persona de Verifik que devuelve
/// nombre — se usa para autopoblar el comprador de la matrícula. Lee document_type y
/// document_number del contexto; hidrata person_full_name / person_first_name /
/// person_last_name (+ person_license_status si está). NO persiste (el handler arma un
/// contexto en memoria). Mode=mock devuelve un nombre sintético determinista para demo
/// sin token. Nunca lanza excepciones de transporte → las mapea a checks.
/// </summary>
internal sealed class VerifikConductorConsultationProvider(
    HttpClient http,
    IOptions<VerifikOptions> verifikOptions,
    IOptions<ConsultationProviderModeOptions> modeOptions) : IConsultationProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly VerifikOptions _opts = verifikOptions.Value;
    private readonly ConsultationProviderModeOptions _modes = modeOptions.Value;

    public string Key => "verifik_conductor";

    public Task<ConsultationResult> ConsultAsync(ConsultationContext ctx, CancellationToken ct)
    {
        if (ConsultationProviderModeOptions.IsMock(_modes.VerifikConductorMode))
            return Task.FromResult(MockResult());

        return RealConsultAsync(ctx, ct);
    }

    private async Task<ConsultationResult> RealConsultAsync(ConsultationContext ctx, CancellationToken ct)
    {
        var documentType = GetValue(ctx, "document_type") ?? GetValue(ctx, "documentType");
        var documentNumber = GetValue(ctx, "document_number") ?? GetValue(ctx, "documentNumber");

        if (string.IsNullOrWhiteSpace(documentType) || string.IsNullOrWhiteSpace(documentNumber))
            return InputError("Se requiere document_type y document_number para consultar RUNT conductor");

        var url = $"/v2/co/runt/conductor?documentType={Uri.EscapeDataString(documentType)}" +
                  $"&documentNumber={Uri.EscapeDataString(documentNumber)}";

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
                return ProviderUnavailable($"Verifik RUNT conductor respondió {(int)response.StatusCode}");

            var payload = await response.Content.ReadFromJsonAsync<VerifikConductorResponse>(JsonOptions, ct);
            if (payload is null)
                return ProviderUnavailable("Respuesta vacía de Verifik RUNT conductor");

            return VerifikConductorResultMapper.Map(payload);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (TaskCanceledException)
        {
            return ProviderUnavailable("Timeout consultando Verifik RUNT conductor");
        }
        catch (HttpRequestException ex)
        {
            return ProviderUnavailable($"Error de red consultando Verifik RUNT conductor: {ex.Message}");
        }
        catch (JsonException)
        {
            return ProviderUnavailable("No se pudo interpretar la respuesta de Verifik RUNT conductor");
        }
    }

    private static ConsultationResult MockResult()
    {
        // Nombre sintético determinista — demoable sin token, obviamente mock.
        var mock = new VerifikConductorResponse
        {
            Data = new VerifikConductorData
            {
                DocumentType = "CC",
                DocumentNumber = "00000000",
                FirstName = "JUAN CARLOS",
                LastName = "PEREZ GOMEZ",
                FullName = "JUAN CARLOS PEREZ GOMEZ",
                IdentityValidationAttempts = new VerifikConductorIdentityValidation
                {
                    EstadoUsuario = "ACTIVO",
                },
            },
        };
        return VerifikConductorResultMapper.Map(mock);
    }

    // Persona no hallada (404): sin campos de nombre + check unknown → el caller lo trata como
    // "not found" y cae al fallback manual. Nunca una excepción.
    private static ConsultationResult NotFound() =>
        new(Key_, "yellow",
            [new ConsultationCheck("conductor", "Persona en RUNT", "unknown", Key_, "Persona no encontrada en RUNT")],
            []);

    private static ConsultationResult ProviderUnavailable(string message) =>
        new(Key_, "yellow",
            [new ConsultationCheck("provider", "Proveedor Verifik RUNT conductor", "unknown", Key_, message)],
            []);

    private static ConsultationResult InputError(string message) =>
        new(Key_, "yellow",
            [new ConsultationCheck("input", "Datos de consulta", "unknown", Key_, message)],
            []);

    private static string? GetValue(ConsultationContext ctx, string key) =>
        ctx.FieldValues.TryGetValue(key, out var v) ? v : null;

    private const string Key_ = "verifik_conductor";
}
