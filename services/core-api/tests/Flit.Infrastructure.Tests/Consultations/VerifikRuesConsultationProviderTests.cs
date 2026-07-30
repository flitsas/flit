using System.Net;
using System.Net.Http.Headers;
using Flit.Infrastructure.Consultations;
using Flit.Tramites.Application.UseCases.Consultations;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Flit.Infrastructure.Tests.Consultations;

/// <summary>
/// HU #10588 — Provider RUES (mock por defecto) que resuelve la plantilla RUES_ACTOR_JURIDICAL
/// para actores persona jurídica (NIT). Feature #10583 (2ª ola).
/// </summary>
public sealed class VerifikRuesConsultationProviderTests
{
    private static readonly Guid InstanceId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid TenantId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static VerifikRuesConsultationProvider BuildProvider(string mode = "mock")
    {
        var http = new HttpClient { BaseAddress = new Uri("https://stub.example.com/") };
        var verifik = Options.Create(new VerifikOptions());
        var modes = Options.Create(new ConsultationProviderModeOptions { VerifikRuesMode = mode });
        return new VerifikRuesConsultationProvider(http, verifik, modes);
    }

    private static ConsultationContext ContextWithNit(string? nit) =>
        new(
            InstanceId,
            TenantId,
            "RUES_ACTOR_JURIDICAL",
            new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            {
                ["nit"] = nit,
            });

    [Fact]
    public void Key_EsVerifikRues()
    {
        BuildProvider().Key.Should().Be("verifik_rues");
    }

    [Fact]
    public async Task ConsultAsync_ModoMock_DevuelveGreenConCheckRuesOk()
    {
        // AC1: NIT (persona jurídica) → el provider responde con los datos del certificado.
        var ct = TestContext.Current.CancellationToken;
        var provider = BuildProvider();

        var result = await provider.ConsultAsync(ContextWithNit("900123456"), ct);

        result.Provider.Should().Be("verifik_rues");
        result.Overall.Should().Be("green");
        result.Checks.Should().ContainSingle();
        result.Checks[0].Key.Should().Be("rues");
        result.Checks[0].Status.Should().Be("ok");
    }

    [Fact]
    public async Task ConsultAsync_ModoMock_HidrataDatosDeLaEmpresaYNitDelContexto()
    {
        var ct = TestContext.Current.CancellationToken;
        var provider = BuildProvider();

        var result = await provider.ConsultAsync(ContextWithNit("900123456"), ct);

        result.HydratedFields.Should().Contain(f => f.FieldKey == "rues_razon_social");
        result.HydratedFields.Should().Contain(f => f.FieldKey == "rues_estado");
        result.HydratedFields.Should().ContainSingle(f => f.FieldKey == "rues_nit")
            .Which.ValueText.Should().Be("900123456");
    }

    [Fact]
    public async Task ConsultAsync_ModoMock_SinNit_NoHidrataNit()
    {
        var ct = TestContext.Current.CancellationToken;
        var provider = BuildProvider();

        var result = await provider.ConsultAsync(ContextWithNit(null), ct);

        result.Overall.Should().Be("green");
        result.HydratedFields.Should().NotContain(f => f.FieldKey == "rues_nit");
    }

    [Fact]
    public async Task ConsultAsync_ModoReal_EnviaV3ConCategoryRmYToken()
    {
        // Ajuste servicio RUES v3: ruta /v3/co/rues-complete, category=RM estático y Bearer token.
        var ct = TestContext.Current.CancellationToken;
        var handler = new CapturingHandler("""
            {"data":{"commercialRegistry":{"NIT":"901691963","businessName":"INVERSIONES ARCINIEGAS S.A.S.",
            "chamberCommerce":"BOGOTA","registrationNumber":"0003650415","registrationStatus":"ACTIVA"}},
            "signature":{"message":"Certified by Verifik.co"},"id":"4YB3V"}
            """);
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.verifik.co/") };
        var verifik = Options.Create(new VerifikOptions { ApiToken = "env-token-123", AuthScheme = "Bearer" });
        var modes = Options.Create(new ConsultationProviderModeOptions { VerifikRuesMode = "real" });
        var provider = new VerifikRuesConsultationProvider(http, verifik, modes);

        var result = await provider.ConsultAsync(ContextWithNit("901691963"), ct);

        // La petición sale bien formada.
        handler.LastRequest.Should().NotBeNull();
        handler.LastRequest!.RequestUri!.AbsolutePath.Should().Be("/v3/co/rues-complete");
        var query = handler.LastRequest.RequestUri.Query;
        query.Should().Contain("documentType=NIT");
        query.Should().Contain("documentNumber=901691963");
        query.Should().Contain("category=RM");
        handler.LastRequest.Headers.Authorization.Should().BeEquivalentTo(
            new AuthenticationHeaderValue("Bearer", "env-token-123"));

        // La respuesta v3 (data.commercialRegistry) se mapea a los campos hidratados.
        result.Overall.Should().Be("green");
        result.Checks[0].Status.Should().Be("ok");
        result.HydratedFields.Should().Contain(f => f.FieldKey == "rues_razon_social" && f.ValueText == "INVERSIONES ARCINIEGAS S.A.S.");
        result.HydratedFields.Should().Contain(f => f.FieldKey == "rues_estado" && f.ValueText == "ACTIVA");
        result.HydratedFields.Should().Contain(f => f.FieldKey == "rues_matricula_mercantil" && f.ValueText == "0003650415");
        result.HydratedFields.Should().Contain(f => f.FieldKey == "rues_camara_comercio" && f.ValueText == "BOGOTA");
    }

    // ── HU #11132 — contrato contra la respuesta REAL del servicio ───────────────────────────────
    //
    // Muestra tomada de una respuesta real de /v3/co/rues-complete. Es la prueba que faltaba: el
    // modelo declaraba seis nombres de campo distintos a los del contrato y, sobre todo, declaraba
    // `legalRepresentatives` como cadena cuando el servicio devuelve un OBJETO. Eso hacía fallar la
    // deserialización completa y la consulta real terminaba en "proveedor no disponible" con cero
    // campos. El mock, construido a mano, reproducía la forma del modelo y ocultaba la divergencia.
    private const string RespuestaReal = """
    {
      "data": {
        "category": "RM",
        "commercialRegistry": {
          "NIT": "900511343",
          "acronym": "",
          "businessName": "CI TRADE ZONE SAS",
          "chamberCity": "Bogotá",
          "chamberCommerce": "BOGOTA",
          "chamberDepartment": "Cundinamarca",
          "commercialAddress": "",
          "companyLocation": "BOGOTA",
          "companyType": "SOCIEDADES POR ACCIONES SIMPLIFICADAS SAS",
          "email": "",
          "enrollmentDate": "2012-03-26",
          "idRm": "40002197149",
          "lastRenewedYear": "2012",
          "lastUpdatedDate": "2018-07-18",
          "legalRepresentatives": {
            "faculty": "** NOMBRAMIENTOS ** QUE POR DOCUMENTO PRIVADO NO. DE ASAMBLEA DE ACCIONISTAS DEL 20 DEMARZO DE 2012",
            "legalRepresentatives": [
              { "documentNumber": "000000052082029", "documentType": "CC", "name": "PADILLA HERNANDEZ ALEXANDRA", "role": "Representante Legal" }
            ]
          },
          "organizationType": "SOCIEDAD ó PERSONA JURIDICA PRINCIPAL ó ESAL",
          "reasonForCancellation": "SOCIEDAD COMERCIAL",
          "registrationNumber": "0002197149",
          "registrationStatus": "MATRÍCULA CANCELADA POR TRASLADO DE DOMICILIO",
          "renewalDate": "Invalid date"
        },
        "economicActivities": [
          { "code": "4620", "description": "Comercio al por mayor de materias primas", "name": "ciiu_act_econ_pri" },
          { "code": "", "description": "", "name": "ciiu4" }
        ]
      },
      "signature": { "dateTime": "July 30, 2026 4:31 PM", "message": "Certified by Verifik.co" },
      "id": "GF42M"
    }
    """;

    private static async Task<ConsultationResult> ConsultarReal(string json, string nit)
    {
        var handler = new CapturingHandler(json);
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.verifik.co/") };
        var verifik = Options.Create(new VerifikOptions { ApiToken = "t", AuthScheme = "Bearer" });
        var modes = Options.Create(new ConsultationProviderModeOptions { VerifikRuesMode = "real" });
        var provider = new VerifikRuesConsultationProvider(http, verifik, modes);
        return await provider.ConsultAsync(ContextWithNit(nit), TestContext.Current.CancellationToken);
    }

    private static string? Valor(ConsultationResult r, string key) =>
        r.HydratedFields.FirstOrDefault(f => f.FieldKey == key)?.ValueText;

    [Fact]
    public async Task ConsultAsync_RespuestaReal_NoFallaLaDeserializacionPorLaRepresentacionLegal()
    {
        // El fallo original: `legalRepresentatives` es un objeto y el modelo lo declaraba string →
        // JsonException → se capturaba como fallo de transporte → check "error" y CERO campos.
        var result = await ConsultarReal(RespuestaReal, "900511343");

        result.Checks.Should().ContainSingle();
        result.Checks[0].Status.Should().NotBe("error", "una respuesta 200 bien formada no puede reportarse como proveedor caído");
        result.HydratedFields.Should().NotBeEmpty();
        Valor(result, "rues_representacion_legal").Should().StartWith("** NOMBRAMIENTOS **");
    }

    [Theory]
    [InlineData("rues_sigla", null)]                                   // acronym: viene vacío → null
    [InlineData("rues_municipio", "BOGOTA")]                           // companyLocation (antes: city)
    [InlineData("rues_fecha_matricula", "2012-03-26")]                 // enrollmentDate (antes: registrationDate)
    [InlineData("rues_camara_ciudad", "Bogotá")]
    [InlineData("rues_camara_departamento", "Cundinamarca")]
    [InlineData("rues_categoria", "Registro Mercantil")]               // derivado de data.category
    [InlineData("rues_actividad_economica", "4620 - Comercio al por mayor de materias primas")]
    public async Task ConsultAsync_RespuestaReal_MapeaLosCamposQueAntesSalianEnBlanco(string llave, string? esperado)
    {
        var result = await ConsultarReal(RespuestaReal, "900511343");

        Valor(result, llave).Should().Be(esperado);
    }

    [Fact]
    public async Task ConsultAsync_RespuestaReal_DescartaElCentinelaDeFechaInvalida()
    {
        // "Invalid date" llegaba intacto al PDF: el normalizador documental conserva el original
        // cuando no puede interpretarlo, así que la celda del certificado decía "Invalid date".
        var result = await ConsultarReal(RespuestaReal, "900511343");

        Valor(result, "rues_fecha_renovacion").Should().BeNull();
    }

    [Fact]
    public async Task ConsultAsync_RespuestaReal_DeserializaLasActividadesEconomicas()
    {
        var result = await ConsultarReal(RespuestaReal, "900511343");

        Valor(result, "rues_actividades_json").Should().NotBeNull()
            .And.Subject.As<string>().Should().Contain("4620");
    }

    [Fact]
    public async Task ConsultAsync_MockYReal_ProducenElMismoJuegoDeLlaves()
    {
        // Guardia contra la causa raíz: mientras el mock se construía a mano, podía divergir del
        // contrato real sin que nada lo notara. Ahora ambos recorren la misma deserialización.
        var ct = TestContext.Current.CancellationToken;
        var mock = await BuildProvider().ConsultAsync(ContextWithNit("900511343"), ct);
        var real = await ConsultarReal(RespuestaReal, "900511343");

        var llavesMock = mock.HydratedFields.Select(f => f.FieldKey).OrderBy(k => k, StringComparer.Ordinal);
        var llavesReal = real.HydratedFields.Select(f => f.FieldKey).OrderBy(k => k, StringComparer.Ordinal);

        llavesMock.Should().Equal(llavesReal);
    }

    [Fact]
    public async Task ConsultAsync_ModoMock_PoblaLosCamposDelCertificado()
    {
        // El mock ahora parte de JSON con la forma real; si un JsonPropertyName vuelve a divergir,
        // estos campos se vacían y el fallo se ve en DEV en vez de solo en producción.
        var ct = TestContext.Current.CancellationToken;

        var result = await BuildProvider().ConsultAsync(ContextWithNit("900123456"), ct);

        Valor(result, "rues_sigla").Should().Be("EMPRESA DEMO");
        Valor(result, "rues_direccion").Should().Be("CL 1 D No 20 - 45");
        Valor(result, "rues_municipio").Should().Be("BOGOTA");
        Valor(result, "rues_fecha_matricula").Should().Be("2023-06-22");
        Valor(result, "rues_representacion_legal").Should().NotBeNullOrWhiteSpace();
        Valor(result, "rues_actividades_json").Should().NotBeNullOrWhiteSpace();
    }

    private sealed class CapturingHandler(string responseJson) : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson, System.Text.Encoding.UTF8, "application/json"),
            });
        }
    }
}
