using System.Net;
using System.Text;
using Flit.Infrastructure.Consultations;
using Flit.Infrastructure.Improntas;
using Flit.Infrastructure.KyverumRunt;
using Flit.Tramites.Application.UseCases.Consultations;
using Flit.Tramites.Domain.Certifications;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Flit.Infrastructure.Tests.Consultations;

/// <summary>
/// HU #14153 AC1 — los proveedores de conductor (<c>kyverum_runt_conductor</c>, <c>verifik_conductor</c>) en modo real llenan
/// <see cref="ConsultationResult.RawPayload"/> con el cuerpo HTTP TAL CUAL llegó (mismo patrón que <c>verifik_rues</c>), sin cambiar
/// nada de lo que Trámites usa hoy del resultado: semáforo, chequeos, campos hidratados y <c>Certifications</c> (que sigue en null,
/// así la ingesta canónica de Trámites no se dispara). Sin respuesta útil (404, 5xx, <c>ok:false</c>) no hay crudo. Datos ficticios
/// (99999…, nombres «PERSONA FICTICIA»).
/// <para>Uso de ejemplo: <c>(await Kyverum(Json(200, Cuerpo)).ConsultAsync(Ctx(), ct)).RawPayload!.PayloadJson</c> → el cuerpo exacto.</para>
/// </summary>
public sealed class RespuestaCrudaConductorTests
{
    private const string Documento = "9999900141";

    private static readonly System.Text.Json.JsonSerializerOptions Web = new(System.Text.Json.JsonSerializerDefaults.Web);

    // Claves que el DTO no declara (numeroLicencia en detalle, restricciones, fechaExpedicion, firma) deben sobrevivir en el crudo.
    private const string CuerpoKyverum = """
        {"ok":true,"identidad":{"primerNombre":"PERSONA","segundoNombre":"","primerApellido":"FICTICIA","segundoApellido":"UNO","nombreCompleto":"PERSONA FICTICIA UNO"},"persona":{"nombres":"P****A","apellidos":"F****A","tipoDocumento":"C","documento":"9999900141","estadoPersona":"ACTIVA","estadoConductor":"ACTIVO","tieneLicencias":true},"licencias":[{"numeroLicencia":"99999001","estadoLicencia":"ACTIVA","fechaExpedicion":"2020-01-15","organismoTransito":"STRIA FICTICIA","detalleLicencia":[{"categoria":"B1","fechaVencimiento":"2030-01-15","restricciones":"LENTES"}]},{"numeroLicencia":"99999002","estadoLicencia":"SUSPENDIDA","detalleLicencia":[{"categoria":"C1","fechaVencimiento":"2024-05-01"}]}],"multas":{"tieneMultas":"NO","nroPazYSalvo":"PZ-FICTICIO"},"fromCache":false,"campoNuevoDelProveedor":{"anidado":[1,2,3]}}
        """;

    private const string CuerpoVerifik = """
        {"data":{"documentType":"CC","documentNumber":"9999900141","firstName":"PERSONA","lastName":"FICTICIA UNO","fullName":"PERSONA FICTICIA UNO","citizenStatus":"ACTIVA","driverStatus":"ACTIVO","totalLicenses":"2","licenses":[{"category":"B1","status":"ACTIVA","dueDate":"15/01/2030","expeditionDate":"15/01/2020","licenceNumber":"99999001","otExpide":"STRIA FICTICIA","restrictions":"LENTES"},{"category":"C1","status":"VENCIDA","dueDate":"01/05/2024","expeditionDate":"01/05/2014","licenceNumber":"99999002","otExpide":"STRIA FICTICIA"}],"infractions":{"tieneMultas":"NO","nroPazYSalvo":"PZ-FICTICIO"},"identityValidationAttempts":{"estadoUsuario":"ACTIVO"},"claveQueElDtoNoConoce":"valor"},"signature":{"dateTime":"October 28, 2026 9:00 AM","message":"Certified by Verifik.co"},"id":"FICTICIO1"}
        """;

    [Fact]
    public async Task Kyverum_conductor_real_guarda_el_cuerpo_HTTP_tal_cual_y_no_cambia_lo_que_ve_Tramites()
    {
        var ct = TestContext.Current.CancellationToken;

        var result = await Kyverum(Responde(HttpStatusCode.OK, CuerpoKyverum)).ConsultAsync(Ctx(), ct);

        result.RawPayload.Should().NotBeNull("el cuerpo del proveedor se conserva");
        var crudo = result.RawPayload!;
        crudo.PayloadJson.Should().Be(CuerpoKyverum, "byte a byte, sin sanear ni reserializar");
        crudo.ProviderKey.Should().Be("kyverum_runt_conductor");
        crudo.SubjectKind.Should().Be(RawProviderPayload.PersonSubject);
        crudo.SubjectKey.Should().Be(Documento);

        // Lo que Trámites usa del resultado es el del mapper de siempre sobre el mismo cuerpo.
        var hoy = KyverumRuntConductorResultMapper.Map(System.Text.Json.JsonSerializer.Deserialize<KyverumRuntPersonaResponse>(
            CuerpoKyverum, Web)!);
        Serializado(result with { RawPayload = null }).Should().Be(Serializado(hoy), "mismo semáforo, chequeos y campos hidratados");
        result.Certifications.Should().BeNull("sin bundle la ingesta canónica de Trámites no persiste nada");
    }

    [Fact]
    public async Task Verifik_conductor_real_guarda_el_cuerpo_HTTP_tal_cual_y_no_cambia_lo_que_ve_Tramites()
    {
        var ct = TestContext.Current.CancellationToken;

        var result = await Verifik(Responde(HttpStatusCode.OK, CuerpoVerifik)).ConsultAsync(Ctx(), ct);

        result.RawPayload.Should().NotBeNull();
        var crudo = result.RawPayload!;
        crudo.PayloadJson.Should().Be(CuerpoVerifik, "byte a byte, incluidas las claves que el DTO no declara");
        crudo.ProviderKey.Should().Be("verifik_conductor");
        crudo.SubjectKind.Should().Be(RawProviderPayload.PersonSubject);
        crudo.SubjectKey.Should().Be(Documento);

        var hoy = VerifikConductorResultMapper.Map(System.Text.Json.JsonSerializer.Deserialize<VerifikConductorResponse>(
            CuerpoVerifik, Web)!);
        Serializado(result with { RawPayload = null }).Should().Be(Serializado(hoy), "mismo semáforo, chequeos y campos hidratados");
        result.Certifications.Should().BeNull();
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task Sin_respuesta_util_no_hay_crudo(HttpStatusCode estado)
    {
        var ct = TestContext.Current.CancellationToken;
        const string Error = """{"error":{"code":"X","message":"fallo ficticio"}}""";

        (await Kyverum(Responde(estado, Error)).ConsultAsync(Ctx(), ct)).RawPayload.Should().BeNull($"kyverum {estado}");
        (await Verifik(Responde(estado, Error)).ConsultAsync(Ctx(), ct)).RawPayload.Should().BeNull($"verifik {estado}");
    }

    [Fact]
    public async Task Kyverum_ok_false_no_encontrado_no_trae_crudo()
    {
        var ct = TestContext.Current.CancellationToken;

        var result = await Kyverum(Responde(HttpStatusCode.OK, """{"ok":false,"message":"Persona no encontrada en el RUNT"}""")).ConsultAsync(Ctx(), ct);

        result.RawPayload.Should().BeNull();
        result.Checks.Should().ContainSingle(c => c.Status == "unknown");
    }

    [Fact]
    public async Task Verifik_conductor_en_mock_no_inventa_crudo()
    {
        var ct = TestContext.Current.CancellationToken;
        var mock = new VerifikConductorConsultationProvider(
            new HttpClient(Responde(HttpStatusCode.OK, "{}")) { BaseAddress = new Uri("https://api.prueba") },
            Options.Create(new VerifikOptions()), Options.Create(new ConsultationProviderModeOptions { VerifikConductorMode = "mock" }));

        (await mock.ConsultAsync(Ctx(), ct)).RawPayload.Should().BeNull("el mock de Trámites sigue igual");
    }

    /// <summary>
    /// Regresión PDN: Verifik 200 con <c>infractions: []</c> (PPT) no debe mapearse a check error
    /// ni perder el nombre (antes JsonException → ProviderUnavailable → failover a Kyverum vacío).
    /// </summary>
    [Fact]
    public async Task Verifik_conductor_con_infractions_array_vacio_hidrata_nombre()
    {
        var ct = TestContext.Current.CancellationToken;
        const string Cuerpo = """
            {"data":{"documentType":"PPT","documentNumber":"9999900141","firstName":"PERSONA","lastName":"FICTICIA PPT","fullName":"PERSONA FICTICIA PPT","citizenStatus":"ACTIVA","driverStatus":"ACTIVO","licenses":[{"category":"B1","status":"ACTIVA"}],"infractions":[],"identityValidationAttempts":{"estadoUsuario":"ACTIVO"}},"id":"FICTICIO-PPT"}
            """;

        var result = await Verifik(Responde(HttpStatusCode.OK, Cuerpo)).ConsultAsync(Ctx(), ct);

        result.Checks.Should().NotContain(c => c.Status == "error");
        result.HydratedFields.Should().Contain(f => f.FieldKey == "person_full_name" && f.ValueText == "PERSONA FICTICIA PPT");
        result.Overall.Should().Be("green");
    }

    /// <summary>
    /// kyverum-runt #113: cuando el actor tiene PPT, el proveedor de conductor debe enviar
    /// <c>tipoDocumento: "Y"</c> a <c>/v1/personas:consultar</c> (no omitirlo, pues omitirlo hace
    /// que Kyverum busque identidad como cédula <c>C</c>) e hidratar el nombre y estado igual que CC.
    /// </summary>
    [Fact]
    public async Task Kyverum_conductor_con_ppt_envia_tipo_Y_e_hidrata_identidad()
    {
        var ct = TestContext.Current.CancellationToken;
        const string CuerpoPpt = """
            {"ok":true,"identidad":{"primerNombre":"PERSONA","segundoNombre":"","primerApellido":"FICTICIA","segundoApellido":"PPT","nombreCompleto":"PERSONA FICTICIA PPT"},"persona":{"nombres":"P****A","apellidos":"F****A","tipoDocumento":"Y","documento":"9999901","estadoPersona":"ACTIVA","estadoConductor":"ACTIVO","tieneLicencias":true},"licencias":[{"numeroLicencia":"9999901","estadoLicencia":"ACTIVA","detalleLicencia":[{"categoria":"C1"}]}],"multas":{"tieneMultas":"NO","nroPazYSalvo":"PZ-PPT-FICTICIO"},"fromCache":false}
            """;
        var responder = Responde(HttpStatusCode.OK, CuerpoPpt);
        var ctx = new ConsultationContext(Guid.Empty, Guid.Empty, "prueba",
            new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            {
                ["document_type"] = "PPT",
                ["document_number"] = "9999901",
            });

        var result = await Kyverum(responder).ConsultAsync(ctx, ct);

        responder.LastBody.Should().Contain("\"documento\":\"9999901\"");
        responder.LastBody.Should().Contain("\"tipoDocumento\":\"Y\"");
        result.Overall.Should().Be("green");
        result.HydratedFields.Should().Contain(f => f.FieldKey == "person_full_name" && f.ValueText == "PERSONA FICTICIA PPT");
        result.RawPayload!.PayloadJson.Should().Be(CuerpoPpt);
    }

    /// <summary>
    /// En <c>/v1/vehiculos:consultar</c>, Kyverum RUNT solo admite <c>C, E, T, P, N, D, R, PT</c>;
    /// un propietario PPT debe omitir <c>tipoDocumento</c> para evitar 400 <c>VALIDATION_ERROR</c>.
    /// </summary>
    [Fact]
    public async Task Kyverum_vehiculo_con_propietario_ppt_omite_tipoDocumento()
    {
        var ct = TestContext.Current.CancellationToken;
        const string CuerpoVehiculo = """
            {"ok":true,"data":{"vehiculo":{"placa":"FCT001","estadoAutomotor":"ACTIVO","gravamenes":"NO","prendas":"NO"},"tipoDocPropietario":"Y","soat":[],"rtm":[]},"fromCache":false}
            """;
        var responder = Responde(HttpStatusCode.OK, CuerpoVehiculo);
        var provider = new KyverumRuntVehicleConsultationProvider(new KyverumRuntApiClient(
            new HttpClient(responder) { BaseAddress = new Uri("https://runt.kyverum.test") },
            Options.Create(new ImprontaRuntOptions { BaseUrl = "https://runt.kyverum.test", ApiKey = "llave-ficticia" }),
            NullLogger<KyverumRuntApiClient>.Instance));
        var ctx = new ConsultationContext(Guid.Empty, Guid.Empty, "vehiculo",
            new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            {
                ["plate"] = "FCT001",
                ["owner_document_type"] = "PPT",
                ["owner_document_number"] = "9999901",
            });

        var result = await provider.ConsultAsync(ctx, ct);

        responder.LastBody.Should().Contain("\"placa\":\"FCT001\"");
        responder.LastBody.Should().Contain("\"documento\":\"9999901\"");
        responder.LastBody.Should().NotContain("tipoDocumento");
        result.HydratedFields.Should().Contain(f => f.FieldKey == "owner_document_type" && f.ValueText == "PPT");
    }

    private static KyverumRuntConductorConsultationProvider Kyverum(HttpMessageHandler handler) =>
        new(new KyverumRuntApiClient(
            new HttpClient(handler) { BaseAddress = new Uri("https://runt.kyverum.test") },
            Options.Create(new ImprontaRuntOptions { BaseUrl = "https://runt.kyverum.test", ApiKey = "llave-ficticia" }),
            NullLogger<KyverumRuntApiClient>.Instance));

    private static VerifikConductorConsultationProvider Verifik(HttpMessageHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("https://api.prueba") },
            Options.Create(new VerifikOptions { ApiToken = "t", AuthScheme = "Bearer" }),
            Options.Create(new ConsultationProviderModeOptions { VerifikConductorMode = "real" }));

    private static ConsultationContext Ctx() => new(Guid.Empty, Guid.Empty, "prueba",
        new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase) { ["document_type"] = "CC", ["document_number"] = Documento });

    private static string Serializado(ConsultationResult r) =>
        System.Text.Json.JsonSerializer.Serialize(r, Web);

    private static Responder Responde(HttpStatusCode estado, string cuerpo) => new(estado, cuerpo);

    private sealed class Responder(HttpStatusCode estado, string cuerpo) : HttpMessageHandler
    {
        public string? LastBody { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastBody = request.Content?.ReadAsStringAsync(cancellationToken).GetAwaiter().GetResult();
            return Task.FromResult(new HttpResponseMessage(estado) { Content = new StringContent(cuerpo, Encoding.UTF8, "application/json") });
        }
    }
}
