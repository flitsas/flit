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
/// <c>GET /v3/co/rues-complete?documentType=NIT&amp;documentNumber={nit}&amp;category=RM</c> en Verifik
/// (misma config/token que RUNT; <c>category=RM</c> es fijo). NUNCA lanza excepciones de
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
            // Endpoint real Verifik RUES v3 (misma config que RUNT por Verifik: BaseUrl/ApiToken/AuthScheme).
            // category=RM es un parámetro estático obligatorio del servicio v3 (registro mercantil).
            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                $"/v3/co/rues-complete?documentType=NIT&documentNumber={Uri.EscapeDataString(nit)}&category=RM");
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
                Data = new VerifikRuesData
                {
                    CommercialRegistry = new VerifikRuesCommercialRegistry
                    {
                        BusinessName = "EMPRESA DEMO S.A.S.",
                        RegistrationStatus = "ACTIVA",
                        RegistrationNumber = "0000000",
                        ChamberCommerce = "Cámara de Comercio de Bogotá",
                        Nit = nit,
                        Sigla = "EMPRESA DEMO",
                        RegistrationDate = "22 de Junio de 2023",
                        LastRenewedYear = "2026",
                        RenewalDate = "26 de Marzo de 2026",
                        Address = "CL 1 D No 20 - 45",
                        City = "Bogotá D.C.",
                        Category = "4. GRUPO III. Microempresas",
                        EconomicActivity = "6201 - Actividades de desarrollo de sistemas informáticos",
                        OrganizationType = "Sociedad por Acciones Simplificada",
                        CompanyType = "SOCIEDADES POR ACCIONES SIMPLIFICADAS SAS",
                        Email = "contacto@empresademo.co",
                        IdRm = "550000077793",
                        LastUpdatedDate = "26 de Marzo de 2026",
                        ReasonForCancellation = "",
                        LegalRepresentatives =
                            "REPRESENTACIÓN LEGAL (PRINCIPALES): El gerente tendrá y ejercerá legalmente a la "
                            + "sociedad ante las autoridades de cualquier orden y ante otras personas jurídicas o "
                            + "naturales, fuera o dentro de juicio, con amplias facultades generales para el buen "
                            + "desempeño de su cargo, y con los poderes especiales que exige la ley para novar, "
                            + "transigir, comprometer, conciliar, desistir y arbitrar los negocios sociales; recibir "
                            + "bienes en pago, promover acciones judiciales e interponer todos los recursos que "
                            + "fueren procedentes conforme a la ley.",
                    },
                    InfoActivitiesEconomic =
                    [
                        new VerifikRuesActivity { Code = "4620", Name = "ciiu_act_econ_pri", Description = "Comercio al por mayor de materias primas agropecuarias; animales vivos" },
                        new VerifikRuesActivity { Code = "1090", Name = "ciiu_act_econ_sec", Description = "Elaboración de alimentos preparados para animales" },
                        new VerifikRuesActivity { Code = "0144", Name = "ciiu3", Description = "Cría de ganado porcino" },
                        new VerifikRuesActivity { Code = "4923", Name = "ciiu4", Description = "Transporte de carga por carretera" },
                    ],
                },
            },
            nit);

    private static ConsultationResult Map(VerifikRuesResponse payload, string? nit)
    {
        var registry = payload.Data?.CommercialRegistry;
        var razonSocial = string.IsNullOrWhiteSpace(registry?.BusinessName) ? "Sin razón social" : registry!.BusinessName!;
        var estado = string.IsNullOrWhiteSpace(registry?.RegistrationStatus) ? "DESCONOCIDO" : registry!.RegistrationStatus!;
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
            new("rues_matricula_mercantil", registry?.RegistrationNumber, null),
            new("rues_camara_comercio", registry?.ChamberCommerce, null),
            // HU #10856 — resto de la tabla certificadora (existencia y representación legal).
            new("rues_sigla", registry?.Sigla, null),
            new("rues_fecha_matricula", registry?.RegistrationDate, null),
            new("rues_ultimo_ano_renovado", registry?.LastRenewedYear, null),
            new("rues_fecha_renovacion", registry?.RenewalDate, null),
            new("rues_direccion", registry?.Address, null),
            new("rues_municipio", registry?.City, null),
            new("rues_categoria", registry?.Category, null),
            new("rues_actividad_economica", registry?.EconomicActivity, null),
            new("rues_tipo_organizacion", registry?.OrganizationType, null),
            // HU #10589 (Feature #10852) — resto del REGISTRO COMERCIAL + representación legal + actividades.
            new("rues_tipo_compania", registry?.CompanyType, null),
            new("rues_email", registry?.Email, null),
            new("rues_id_rm", registry?.IdRm, null),
            new("rues_fecha_actualizacion", registry?.LastUpdatedDate, null),
            new("rues_razon_cancelacion", registry?.ReasonForCancellation, null),
            new("rues_representacion_legal", registry?.LegalRepresentatives, null),
            new("rues_actividades_json", SerializeActividades(payload.Data?.InfoActivitiesEconomic), null),
        };
        // El NIT del contexto tiene prioridad; si no vino, se usa el que devuelve RUES.
        var nitValue = !string.IsNullOrWhiteSpace(nit) ? nit : registry?.Nit;
        if (!string.IsNullOrWhiteSpace(nitValue))
            hydrated.Add(new HydratedField("rues_nit", nitValue, null));

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

    // HU #10589 — serializa la lista de actividades a JSON compacto (codigo/nombre/descripcion) para
    // persistirla en un único field_value; el generador del certificado la deserializa. Vacía/null → null.
    private static string? SerializeActividades(List<VerifikRuesActivity>? actividades)
    {
        if (actividades is null || actividades.Count == 0)
            return null;
        var items = actividades
            .Select(a => new { codigo = a.Code, nombre = a.Name, descripcion = a.Description })
            .ToList();
        return JsonSerializer.Serialize(items, JsonOptions);
    }

    private const string Key_ = "verifik_rues";
}

/// <summary>
/// Respuesta del endpoint Verifik RUES v3 (<c>GET /v3/co/rues-complete</c>). La info del registro
/// mercantil viaja anidada en <c>data.commercialRegistry</c>; aquí solo se modelan los campos que
/// el certificado/checklist RUES necesita (mode=mock genera esta misma forma).
/// </summary>
internal sealed class VerifikRuesResponse
{
    [JsonPropertyName("data")]
    public VerifikRuesData? Data { get; set; }
}

internal sealed class VerifikRuesData
{
    [JsonPropertyName("commercialRegistry")]
    public VerifikRuesCommercialRegistry? CommercialRegistry { get; set; }

    // HU #10589 (Feature #10852) — lista de actividades económicas (CIIU). Clave aproximada al contrato
    // Verifik RUES v3; si el servicio real usa otra, llega vacía y la sección queda en blanco.
    [JsonPropertyName("infoActivitiesEconomic")]
    public List<VerifikRuesActivity>? InfoActivitiesEconomic { get; set; }
}

internal sealed class VerifikRuesActivity
{
    [JsonPropertyName("code")]
    public string? Code { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }
}

internal sealed class VerifikRuesCommercialRegistry
{
    [JsonPropertyName("businessName")]
    public string? BusinessName { get; set; }

    [JsonPropertyName("registrationStatus")]
    public string? RegistrationStatus { get; set; }

    [JsonPropertyName("registrationNumber")]
    public string? RegistrationNumber { get; set; }

    [JsonPropertyName("chamberCommerce")]
    public string? ChamberCommerce { get; set; }

    [JsonPropertyName("NIT")]
    public string? Nit { get; set; }

    // HU #10856 — campos adicionales de la tabla certificadora (existencia y representación legal).
    // NOTA: los nombres JSON son la mejor aproximación al contrato Verifik RUES v3; si el servicio real
    // usa otras claves, estos campos llegan null y el certificado los deja EN BLANCO (sin romper nada).
    [JsonPropertyName("tradeName")]
    public string? Sigla { get; set; }

    [JsonPropertyName("registrationDate")]
    public string? RegistrationDate { get; set; }

    [JsonPropertyName("lastRenewedYear")]
    public string? LastRenewedYear { get; set; }

    [JsonPropertyName("renewalDate")]
    public string? RenewalDate { get; set; }

    [JsonPropertyName("address")]
    public string? Address { get; set; }

    [JsonPropertyName("city")]
    public string? City { get; set; }

    [JsonPropertyName("category")]
    public string? Category { get; set; }

    [JsonPropertyName("economicActivity")]
    public string? EconomicActivity { get; set; }

    [JsonPropertyName("organizationType")]
    public string? OrganizationType { get; set; }

    // HU #10589 (Feature #10852) — resto del REGISTRO COMERCIAL de la muestra oficial + texto de
    // representación legal. Claves aproximadas al contrato Verifik RUES v3; ausentes → en blanco.
    [JsonPropertyName("companyType")]
    public string? CompanyType { get; set; }

    [JsonPropertyName("email")]
    public string? Email { get; set; }

    [JsonPropertyName("idRm")]
    public string? IdRm { get; set; }

    [JsonPropertyName("lastUpdatedDate")]
    public string? LastUpdatedDate { get; set; }

    [JsonPropertyName("reasonForCancellation")]
    public string? ReasonForCancellation { get; set; }

    [JsonPropertyName("legalRepresentatives")]
    public string? LegalRepresentatives { get; set; }
}
