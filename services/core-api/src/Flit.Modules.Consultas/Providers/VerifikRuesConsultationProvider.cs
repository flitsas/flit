using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Flit.Tramites.Application.UseCases.Consultations;
using Flit.Tramites.Domain.Certifications;
using Flit.Tramites.Domain.Certifications.Normalization;
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

            // HU #11306 — se lee como texto y se deserializa desde ahí para conservar el JSON tal como
            // llegó. Aquí importa el doble: el bloque `legalRepresentatives` trae una lista
            // estructurada de representantes cuya forma real NO está documentada en ninguna captura,
            // y este modelo solo declara `faculty`. Guardar el crudo es lo que permitirá modelarla sin
            // volver a pagar la consulta, en vez de adivinar nombres de campo — que es exactamente el
            // fallo que originó este Feature.
            var raw = await response.Content.ReadAsStringAsync(ct);
            var payload = JsonSerializer.Deserialize<VerifikRuesResponse>(raw, JsonOptions);
            if (payload is null)
                return ProviderUnavailable();

            return Map(payload, nit) with
            {
                RawPayload = new RawProviderPayload(
                    ProviderKey: Key_,
                    SubjectKind: RawProviderPayload.CompanySubject,
                    SubjectKey: nit,
                    PayloadJson: raw,
                    QueriedAt: DateTimeOffset.UtcNow),
            };
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

    /// <summary>
    /// Empresa activa (caso limpio para DEV). El NIT viaja en el contexto; el resto son datos
    /// canónicos que el certificado (HU #10589) renderiza.
    ///
    /// <para><b>HU #11132 — el mock parte de JSON, no de objetos.</b> Antes construía
    /// <see cref="VerifikRuesResponse"/> a mano, así que reproducía la forma que el MODELO espera y no
    /// la que devuelve el servicio: por eso seis nombres de campo equivocados y un cambio de tipo
    /// pasaron meses inadvertidos —en DEV el certificado se veía perfecto y en real la consulta no
    /// devolvía nada—. Deserializando una muestra con la forma REAL, el mock recorre exactamente el
    /// mismo camino que el modo real: si un <c>JsonPropertyName</c> vuelve a divergir, se nota aquí.</para>
    /// </summary>
    private static ConsultationResult MockResult(string? nit)
    {
        var payload = JsonSerializer.Deserialize<VerifikRuesResponse>(MockPayloadJson, JsonOptions)
            ?? new VerifikRuesResponse();

        // El NIT lo manda el contexto, no la muestra: sin NIT en el contexto el mock no debe inventar
        // uno (hay consumidores que distinguen "no se consultó nadie" de "se consultó esta empresa").
        if (payload.Data?.CommercialRegistry is { } registry)
            registry.Nit = nit;

        return Map(payload, nit);
    }

    /// <summary>Muestra con la forma REAL del contrato Verifik RUES v3 (datos de demo).</summary>
    private const string MockPayloadJson = """
    {
      "data": {
        "category": "RM",
        "commercialRegistry": {
          "NIT": "900000000",
          "acronym": "EMPRESA DEMO",
          "businessName": "EMPRESA DEMO S.A.S.",
          "chamberCity": "Bogotá",
          "chamberCommerce": "BOGOTA",
          "chamberDepartment": "Cundinamarca",
          "commercialAddress": "CL 1 D No 20 - 45",
          "companyLocation": "BOGOTA",
          "companyType": "SOCIEDADES POR ACCIONES SIMPLIFICADAS SAS",
          "email": "contacto@empresademo.co",
          "enrollmentDate": "2023-06-22",
          "idRm": "550000077793",
          "lastRenewedYear": "2026",
          "lastUpdatedDate": "2026-03-26",
          "legalRepresentatives": {
            "faculty": "** NOMBRAMIENTOS ** QUE POR DOCUMENTO PRIVADO DE ASAMBLEA DE ACCIONISTAS FUE (RON) NOMBRADO (S): NOMBRE IDENTIFICACION REPRESENTANTE LEGAL DEMO PEREZ ANA C.C. 000000052000000 REPRESENTANTE LEGAL SUPLENTE DEMO GOMEZ LUIS C.C. 000000079000000",
            "legalRepresentatives": [
              { "documentNumber": "000000052000000", "documentType": "CC", "name": "DEMO PEREZ ANA", "role": "Representante Legal" },
              { "documentNumber": "000000079000000", "documentType": "CC", "name": "SUPLENTE DEMO GOMEZ LUIS", "role": "Representante Legal" }
            ]
          },
          "organizationType": "SOCIEDAD ó PERSONA JURIDICA PRINCIPAL ó ESAL",
          "reasonForCancellation": "",
          "registrationNumber": "0000000",
          "registrationStatus": "ACTIVA",
          "renewalDate": "2026-03-26"
        },
        "economicActivities": [
          { "code": "4620", "description": "Comercio al por mayor de materias primas agropecuarias; animales vivos", "name": "ciiu_act_econ_pri" },
          { "code": "1090", "description": "Elaboración de alimentos preparados para animales", "name": "ciiu_act_econ_sec" },
          { "code": "0144", "description": "Cría de ganado porcino", "name": "ciiu3" },
          { "code": "4923", "description": "Transporte de carga por carretera", "name": "ciiu4" }
        ]
      },
      "signature": { "dateTime": "July 30, 2026 4:31 PM", "message": "Certified by Verifik.co" },
      "id": "DEMO1"
    }
    """;

    private static ConsultationResult Map(VerifikRuesResponse payload, string? nit)
    {
        var registry = payload.Data?.CommercialRegistry;
        // HU #11306 — se RETIRAN los rellenos "Sin razón social" y "DESCONOCIDO". Parecían inocuos y
        // no lo eran: se escribían en field_values como si fueran datos del RUES, hacían pasar el
        // criterio de emisión del certificado y producían un documento oficial que afirmaba, en la
        // casilla de razón social, la frase "Sin razón social". Ausente ⇒ celda en blanco y, si no hay
        // razón social, no se emite el certificado (regla HU #10856 llevada hasta el final).
        var razonSocial = Limpio(registry?.BusinessName);
        var estado = Limpio(registry?.RegistrationStatus);
        var activa = string.Equals(estado, "ACTIVA", StringComparison.OrdinalIgnoreCase);

        var check = new ConsultationCheck(
            "rues",
            "Consulta RUES",
            activa ? "ok" : "warn",
            Key_,
            activa
                ? $"Empresa activa en RUES: {razonSocial}"
                : estado is not null
                    ? $"Estado en RUES: {estado}"
                    : "El RUES no reportó el estado de la empresa");

        var actividades = payload.Data?.EconomicActivities;

        var hydrated = new List<HydratedField>
        {
            new("rues_razon_social", razonSocial, null),
            new("rues_estado", estado, null),
            new("rues_matricula_mercantil", Limpio(registry?.RegistrationNumber), null),
            new("rues_camara_comercio", Limpio(registry?.ChamberCommerce), null),
            // HU #10856 — resto de la tabla certificadora (existencia y representación legal).
            new("rues_sigla", Limpio(registry?.Sigla), null),
            new("rues_fecha_matricula", Limpio(registry?.RegistrationDate), null),
            new("rues_ultimo_ano_renovado", Limpio(registry?.LastRenewedYear), null),
            new("rues_fecha_renovacion", Limpio(registry?.RenewalDate), null),
            new("rues_direccion", Limpio(registry?.Address), null),
            new("rues_municipio", Limpio(registry?.City), null),
            // HU #11132 — el servicio no entrega estos dos como campo propio; se DERIVAN de datos que sí
            // trae, en vez de dejar dos celdas del certificado condenadas a salir siempre en blanco.
            new("rues_categoria", CategoriaLegible(payload.Data?.Category), null),
            new("rues_actividad_economica", ActividadPrincipal(actividades), null),
            new("rues_tipo_organizacion", Limpio(registry?.OrganizationType), null),
            // HU #10589 (Feature #10852) — resto del REGISTRO COMERCIAL + representación legal + actividades.
            new("rues_tipo_compania", Limpio(registry?.CompanyType), null),
            new("rues_email", Limpio(registry?.Email), null),
            new("rues_id_rm", Limpio(registry?.IdRm), null),
            new("rues_fecha_actualizacion", Limpio(registry?.LastUpdatedDate), null),
            new("rues_razon_cancelacion", Limpio(registry?.ReasonForCancellation), null),
            new("rues_representacion_legal", Limpio(registry?.LegalRepresentatives?.Faculty), null),
            new("rues_camara_ciudad", Limpio(registry?.ChamberCity), null),
            new("rues_camara_departamento", Limpio(registry?.ChamberDepartment), null),
            new("rues_actividades_json", SerializeActividades(actividades), null),
        };
        // El NIT del contexto tiene prioridad; si no vino, se usa el que devuelve RUES.
        var nitValue = !string.IsNullOrWhiteSpace(nit) ? nit : registry?.Nit;
        if (!string.IsNullOrWhiteSpace(nitValue))
            hydrated.Add(new HydratedField("rues_nit", nitValue, null));

        return new ConsultationResult(
            Key_, activa ? "green" : "yellow", [check], hydrated,
            Certifications: BuildBundle(registry, nitValue, razonSocial, estado, payload));
    }

    /// <summary>
    /// Traduce el registro mercantil al modelo canónico (HU #11306, ADR-0041), para que el certificado
    /// deje de depender de un snapshot congelado en <c>field_values</c> — que es inmutable fuera de
    /// borrador y por eso obligaba a consultar en vivo al generar el PDF.
    /// </summary>
    /// <remarks>
    /// <see cref="MerchantRegistration.LegalRepresentatives"/> queda <b>vacío</b> a propósito: el
    /// servicio entrega una lista estructurada de representantes, pero no hay ninguna captura real que
    /// documente sus nombres de campo, y este modelo solo declara <c>faculty</c>. Inventarlos sería
    /// repetir el defecto que originó el Feature. El payload crudo ya se persiste, así que modelarla
    /// después no costará una nueva consulta.
    /// </remarks>
    private static CertificationBundle? BuildBundle(
        VerifikRuesCommercialRegistry? registry, string? nit, string? razonSocial, string? estado,
        VerifikRuesResponse payload)
    {
        if (string.IsNullOrWhiteSpace(nit))
            return null;

        var registration = new MerchantRegistration(
            nit.Trim(),
            EntityNameNormalizer.Normalize(razonSocial),
            CertificateNumberNormalizer.Normalize(Limpio(registry?.RegistrationNumber)),
            VigencyStatusNormalizer.ForMerchantRegistration(estado),
            ColombianCertificateDate.Parse(Limpio(registry?.RegistrationDate)),
            ColombianCertificateDate.Parse(Limpio(registry?.RenewalDate)),
            EntityNameNormalizer.Normalize(Limpio(registry?.ChamberCommerce)),
            EntityNameNormalizer.Normalize(CategoriaLegible(payload.Data?.Category)),
            EntityNameNormalizer.Normalize(Limpio(registry?.Address)),
            EntityNameNormalizer.Normalize(Limpio(registry?.City)),
            []);

        return registration.HasAnyValue ? CertificationBundle.ForCompany(registration) : null;
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

    /// <summary>
    /// HU #11132 — descarta los valores CENTINELA del proveedor. El RUES devuelve literalmente
    /// <c>"Invalid date"</c> en fechas que no tiene, y ese texto llegaba tal cual al PDF: el
    /// normalizador documental conserva el original cuando no puede interpretarlo, y hacía bien
    /// (nunca inventa un dato), pero el centinela no es un dato. Se corta en el origen para que
    /// ningún consumidor —certificado, wizard o caché— lo herede.
    /// </summary>
    private static string? Limpio(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var raw = value.Trim();
        return Centinelas.Contains(raw) ? null : raw;
    }

    private static readonly HashSet<string> Centinelas = new(StringComparer.OrdinalIgnoreCase)
    {
        "invalid date",
        "null",
        "undefined",
        "n/a",
    };

    /// <summary>
    /// "Categoría Inscripción" del certificado. El servicio no la entrega como campo; sí devuelve la
    /// categoría del registro consultado (<c>RM</c> = registro mercantil), que es lo que la celda
    /// significa. Un código desconocido se imprime tal cual antes que dejarse en blanco.
    /// </summary>
    private static string? CategoriaLegible(string? category)
    {
        var raw = Limpio(category);
        if (raw is null)
            return null;

        return raw.ToUpperInvariant() switch
        {
            "RM" => "Registro Mercantil",
            "ESAL" => "Entidad Sin Ánimo de Lucro",
            "RNT" => "Registro Nacional de Turismo",
            _ => raw,
        };
    }

    /// <summary>
    /// "Actividad Económica" del certificado: la CIIU PRINCIPAL de la lista de actividades
    /// (<c>ciiu_act_econ_pri</c>), formateada "código - descripción". Tampoco existe como campo propio
    /// en la respuesta. Si la actividad principal viene sin código ni descripción, la celda queda vacía.
    /// </summary>
    private static string? ActividadPrincipal(List<VerifikRuesActivity>? actividades)
    {
        var principal = actividades?.FirstOrDefault(a =>
            string.Equals(a.Name, "ciiu_act_econ_pri", StringComparison.OrdinalIgnoreCase));
        if (principal is null)
            return null;

        var codigo = Limpio(principal.Code);
        var descripcion = Limpio(principal.Description);

        if (codigo is null)
            return descripcion;

        return descripcion is null ? codigo : $"{codigo} - {descripcion}";
    }

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

    // HU #11132 — la lista de actividades económicas (CIIU) viaja en `economicActivities`. Antes se
    // leía `infoActivitiesEconomic`, que no existe en el contrato: la tabla salía SIEMPRE vacía.
    [JsonPropertyName("economicActivities")]
    public List<VerifikRuesActivity>? EconomicActivities { get; set; }

    /// <summary>Categoría del registro consultado ("RM" = registro mercantil). Es el `category` de la petición.</summary>
    [JsonPropertyName("category")]
    public string? Category { get; set; }
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

    // HU #11132 — nombres REALES del contrato Verifik RUES v3, verificados contra una respuesta del
    // servicio. Los anteriores eran una aproximación y ninguno de estos cinco casaba, así que sus
    // celdas del certificado salían siempre en blanco.
    [JsonPropertyName("acronym")]
    public string? Sigla { get; set; }

    [JsonPropertyName("enrollmentDate")]
    public string? RegistrationDate { get; set; }

    [JsonPropertyName("lastRenewedYear")]
    public string? LastRenewedYear { get; set; }

    [JsonPropertyName("renewalDate")]
    public string? RenewalDate { get; set; }

    [JsonPropertyName("commercialAddress")]
    public string? Address { get; set; }

    [JsonPropertyName("companyLocation")]
    public string? City { get; set; }

    /// <summary>Ciudad de la cámara de comercio (dato propio, distinto de la ubicación de la empresa).</summary>
    [JsonPropertyName("chamberCity")]
    public string? ChamberCity { get; set; }

    [JsonPropertyName("chamberDepartment")]
    public string? ChamberDepartment { get; set; }

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

    /// <summary>
    /// HU #11132 — <b>este campo es un OBJETO, no una cadena.</b> Declararlo como <c>string?</c> hacía
    /// que <c>System.Text.Json</c> lanzara <c>JsonException</c> al deserializar, la excepción se
    /// capturaba como fallo de transporte y la consulta REAL devolvía "proveedor no disponible" con
    /// CERO campos. No era que el certificado saliera incompleto: no llegaba ningún dato.
    /// </summary>
    [JsonPropertyName("legalRepresentatives")]
    public VerifikRuesLegalRepresentation? LegalRepresentatives { get; set; }
}

/// <summary>
/// Bloque de representación legal. Solo se modela <c>faculty</c>, que es lo que pinta el certificado.
/// El servicio trae además una lista estructurada de representantes (nombre, tipo y número de
/// documento, rol) que hoy NO se consume: pintarla es una decisión de layout pendiente del PO, y
/// modelarla sin consumidor sería código muerto. Las claves no modeladas se ignoran al deserializar.
/// </summary>
internal sealed class VerifikRuesLegalRepresentation
{
    [JsonPropertyName("faculty")]
    public string? Faculty { get; set; }
}
