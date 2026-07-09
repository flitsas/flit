using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Flit.Tramites.Application.UseCases.Consultations;
using Microsoft.Extensions.Options;

namespace Flit.Infrastructure.Consultations;

/// <summary>
/// Proveedor RUES (Registro Único Empresarial y Social): consulta persona jurídica por NIT.
/// Resuelve el template <c>RUES_ACTOR_JURIDICAL</c> (entity_scope=actor, person_type=juridical).
/// Mode=mock (default) devuelve datos canónicos de una empresa activa con la misma forma del
/// contrato → swap transparente al modo real (<c>VERIFIK_RUES_MODE=real</c>). El modo real consulta
/// <c>GET /v2/co/rues/complete</c> en Verifik (misma config que RUNT). NUNCA lanza excepciones de
/// transporte al handler (contrato <see cref="IConsultationProvider"/>): errores de red se
/// mapean a un check "error" (bloqueo duro). El certificado RUES en PDF se genera e incorpora
/// al consolidado en HU #10589.
/// </summary>
internal sealed class VerifikRuesConsultationProvider(
    HttpClient http,
    IOptions<VerifikOptions> verifikOptions,
    IOptions<ConsultationProviderModeOptions> modeOptions) : IConsultationProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly VerifikOptions _opts = verifikOptions.Value;
    private readonly ConsultationProviderModeOptions _modes = modeOptions.Value;

    public string Key => Key_;

    public Task<ConsultationResult> ConsultAsync(ConsultationContext ctx, CancellationToken ct)
    {
        if (ConsultationProviderModeOptions.IsMock(_modes.VerifikRuesMode))
            return Task.FromResult(MockResult(ResolveNit(ctx)));

        return RealConsultAsync(ctx, ct);
    }

    private async Task<ConsultationResult> RealConsultAsync(ConsultationContext ctx, CancellationToken ct)
    {
        var nit = ResolveNit(ctx);
        if (string.IsNullOrWhiteSpace(nit))
            return InputError("Se requiere el NIT para consultar RUES");

        try
        {
            // Endpoint real Verifik RUES (misma config que RUNT por Verifik: BaseUrl/ApiToken/AuthScheme).
            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                $"/v2/co/rues/complete?documentType=NIT&documentNumber={Uri.EscapeDataString(nit)}");
            if (!string.IsNullOrWhiteSpace(_opts.ApiToken))
                request.Headers.Authorization = new AuthenticationHeaderValue(_opts.AuthScheme, _opts.ApiToken);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            using var response = await http.SendAsync(request, ct);

            if (response.StatusCode == HttpStatusCode.NotFound)
                return NotFound(nit);

            if (!response.IsSuccessStatusCode)
                return ProviderUnavailable();

            var payload = await response.Content.ReadFromJsonAsync<VerifikRuesResponse>(JsonOptions, ct);
            if (payload is null)
                return ProviderUnavailable();

            return Map(payload, nit);
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

    // Empresa activa (caso limpio para DEV). El NIT viaja en el contexto; el resto son datos
    // canónicos que HU #10589 usará para renderizar el certificado.
    private static ConsultationResult MockResult(string? nit) =>
        Map(
            new VerifikRuesResponse
            {
                RazonSocial = "EMPRESA DEMO S.A.S.",
                Estado = "ACTIVA",
                MatriculaMercantil = "0000000",
                CamaraComercio = "Cámara de Comercio de Bogotá",
                Nit = nit,
            },
            nit);

    private static ConsultationResult Map(VerifikRuesResponse payload, string? nit)
    {
        var razonSocial = string.IsNullOrWhiteSpace(payload.RazonSocial) ? "Sin razón social" : payload.RazonSocial!;
        var estado = string.IsNullOrWhiteSpace(payload.Estado) ? "DESCONOCIDO" : payload.Estado!;
        var activa = estado.Equals("ACTIVA", StringComparison.OrdinalIgnoreCase);

        var check = new ConsultationCheck(
            "rues",
            "Consulta RUES",
            activa ? "ok" : "warn",
            Key_,
            activa
                ? $"Empresa activa en RUES: {razonSocial}"
                : $"Estado en RUES: {estado}");

        var hydrated = new List<HydratedField>
        {
            new("rues_razon_social", razonSocial, null),
            new("rues_estado", estado, null),
            new("rues_matricula_mercantil", payload.MatriculaMercantil, null),
            new("rues_camara_comercio", payload.CamaraComercio, null),
        };
        if (!string.IsNullOrWhiteSpace(nit))
            hydrated.Add(new HydratedField("rues_nit", nit, null));

        return new ConsultationResult(Key_, activa ? "green" : "yellow", [check], hydrated);
    }

    private static ConsultationResult NotFound(string nit) =>
        new(Key_, "yellow",
            [new ConsultationCheck("rues", "Consulta RUES", "unknown", Key_,
                $"No se encontró la empresa con NIT {nit} en RUES")],
            []);

    // No se pudo verificar RUES (no-200/timeout/red/respuesta ilegible): check "error"
    // (bloqueo duro, no subsanable) con mensaje amigable que no expone el proveedor.
    private static ConsultationResult ProviderUnavailable() =>
        new(Key_, "red",
            [new ConsultationCheck("provider", "Consulta RUES", "error", Key_,
                "No fue posible verificar la información en RUES en este momento. Vuelve a intentarlo en unos minutos.")],
            []);

    private static ConsultationResult InputError(string message) =>
        new(Key_, "yellow",
            [new ConsultationCheck("input", "Datos de consulta", "unknown", Key_, message)],
            []);

    private static string? ResolveNit(ConsultationContext ctx) =>
        GetValue(ctx, "nit")
        ?? GetValue(ctx, "actor_document_number")
        ?? GetValue(ctx, "documentNumber");

    private static string? GetValue(ConsultationContext ctx, string key) =>
        ctx.FieldValues.TryGetValue(key, out var v) ? v : null;

    private const string Key_ = "verifik_rues";
}

/// <summary>
/// Respuesta mínima del endpoint RUES (contrato provisional hasta que exista el proveedor real;
/// mode=mock genera esta misma forma). Los nombres cubren lo que el certificado RUES necesita.
/// </summary>
internal sealed class VerifikRuesResponse
{
    [JsonPropertyName("razonSocial")]
    public string? RazonSocial { get; set; }

    [JsonPropertyName("estado")]
    public string? Estado { get; set; }

    [JsonPropertyName("matriculaMercantil")]
    public string? MatriculaMercantil { get; set; }

    [JsonPropertyName("camaraComercio")]
    public string? CamaraComercio { get; set; }

    [JsonPropertyName("nit")]
    public string? Nit { get; set; }
}
