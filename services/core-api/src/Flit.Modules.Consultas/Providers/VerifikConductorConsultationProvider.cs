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
            return Task.FromResult(MockResult(GetValue(ctx, "document_number") ?? GetValue(ctx, "documentNumber")));

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
                return ProviderUnavailable();

            var payload = await response.Content.ReadFromJsonAsync<VerifikConductorResponse>(JsonOptions, ct);
            if (payload is null)
                return ProviderUnavailable();

            return VerifikConductorResultMapper.Map(payload);
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

    private static ConsultationResult MockResult(string? documentNumber = null)
    {
        // Sentinel de prueba (paralelo al SNV* del SOAT en el mock Verifik vehículo): un documento que
        // empieza con '9999' simula un conductor CON multas (paz y salvo NO), para poder probar la
        // advertencia INFORMATIVA del DRIVER (que registra la novedad pero NO bloquea el trámite). El
        // resto queda a paz y salvo.
        var tieneMultas = (documentNumber ?? string.Empty).StartsWith("9999", StringComparison.Ordinal) ? "SI" : "NO";

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
                CitizenStatus = "ACTIVA",
                DriverStatus = "ACTIVO",
                TotalLicenses = "1",
                Licenses =
                [
                    new VerifikConductorLicense
                    {
                        Category = "B1",
                        Status = "ACTIVA",
                        DueDate = "19/09/2034",
                        ExpeditionDate = "19/09/2024",
                        LicenceNumber = "00000000",
                        OtExpide = "SECRETARIA DISTRITAL DE MOVILIDAD",
                    }
                ],
                Infractions = new VerifikConductorInfractions
                {
                    TieneMultas = tieneMultas,
                    NroPazYSalvo = tieneMultas == "SI" ? string.Empty : "DEMO-PAZ-Y-SALVO"
                },
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

    // No se pudo consultar la persona en el RUNT (no-200/timeout/red/respuesta ilegible).
    // Check "error" (contrato consistente); el lookup de persona solo lee HydratedFields y cae
    // al ingreso manual, así que esto no bloquea la identidad, solo evita exponer detalle crudo.
    private static ConsultationResult ProviderUnavailable() =>
        new(Key_, "red",
            [new ConsultationCheck("provider", "Consulta de persona (RUNT)", "error", Key_,
                "No fue posible verificar la información en el RUNT en este momento. Vuelve a intentarlo en unos minutos.")],
            []);

    private static ConsultationResult InputError(string message) =>
        new(Key_, "yellow",
            [new ConsultationCheck("input", "Datos de consulta", "unknown", Key_, message)],
            []);

    private static string? GetValue(ConsultationContext ctx, string key) =>
        ctx.FieldValues.TryGetValue(key, out var v) ? v : null;

    private const string Key_ = "verifik_conductor";
}
