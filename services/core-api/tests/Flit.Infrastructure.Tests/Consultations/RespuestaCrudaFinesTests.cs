using System.Net;
using System.Text;
using Flit.Infrastructure.Consultations;
using Flit.Tramites.Application.UseCases.Consultations;
using Flit.Tramites.Domain.Certifications;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Flit.Infrastructure.Tests.Consultations;

/// <summary>
/// HU #14183 T1 — los tres proveedores de multas (VerifikSimit, KyverumFines, FlitFines) en modo real
/// llenan <see cref="ConsultationResult.RawPayload"/> con el cuerpo HTTP TAL CUAL llegó, con el mismo
/// patrón que conductor y RUES (HU #14153). El semáforo, los comparendos normalizados y los chequeos
/// no cambian. El 404 de Verifik SIMIT («sin registros») también guarda el cuerpo. Sin respuesta útil
/// (5xx) no hay crudo. Datos ficticios (99999…, TST###, nombres «FICTICI…»).
/// <para>Uso de ejemplo:
/// <c>(await Simit(Responde(200, CuerpoPersona)).ConsultAsync(CtxDocumento(), ct)).RawPayload!.PayloadJson</c>
/// → el cuerpo exacto.</para>
/// </summary>
public sealed class RespuestaCrudaFinesTests
{
    private static readonly System.Text.Json.JsonSerializerOptions Web = new(System.Text.Json.JsonSerializerDefaults.Web);

    private const string Documento = "9999900016";
    private const string Placa = "TST201";

    // Persona con multa en cobro coactivo (§ 3.1 del plan).
    // Campos que el DTO no declara (estadoCartera, idOrganismoTransito, infractor, firma) sobreviven en el crudo.
    private const string CuerpoPersona = """
        {"data":{"multas":[{"estadoComparendo":null,"estadoCartera":"Cobro coactivo","valor":"344728","valorPagar":"922115","organismoTransito":"Cisneros","numeroComparendo":"513","fechaComparendo":"31/07/2016 00:00:00","infractor":{"tipoDocumento":"Cédula","numeroDocumento":"9999900016","nombre":"NO*** FI***","apellido":"AP**** FI***"},"infracciones":[{"codigoInfraccion":"C24","descripcionInfraccion":"Conducir motocicleta sin observar las normas establecidas."}]}],"comparendos":[],"cursos":[],"acuerdosPago":[],"totalMultasPagar":"1","cantMultasPagar":"0"},"signature":{"dateTime":"October 29, 2026 2:07 PM","message":"Certified by Verifik.co"},"id":"EJEMPLO-PERSONA"}
        """;

    // Vehículo sin multas (§ 3.1 del plan).
    private const string CuerpoVehiculo = """
        {"data":{"multas":[],"comparendos":[],"cursos":[],"acuerdosPago":[],"plate":"TST201","paramEntradaPlaca":true,"totalMultasPagar":"0","cantMultasPagar":"0"},"signature":{"dateTime":"October 29, 2026 2:08 PM","message":"Certified by Verifik.co"},"id":"EJEMPLO-VEHICULO"}
        """;

    // Cuerpo que Verifik retorna con HTTP 404 («sin registros SIMIT»).
    private const string CuerpoVerifik404 = """
        {"message":"No se encontraron registros para los criterios de búsqueda indicados.","code":"NOT_FOUND"}
        """;

    // ── Verifik SIMIT ────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task VerifikSimit_real_por_documento_guarda_el_cuerpo_HTTP_y_no_cambia_semaforo_ni_comparendos()
    {
        var ct = TestContext.Current.CancellationToken;

        var result = await Simit(Responde(HttpStatusCode.OK, CuerpoPersona)).ConsultAsync(CtxDocumento(), ct);

        result.RawPayload.Should().NotBeNull("el cuerpo del proveedor se conserva");
        var crudo = result.RawPayload!;
        crudo.PayloadJson.Should().Be(CuerpoPersona.Trim(), "byte a byte, sin sanear ni reserializar");
        crudo.ProviderKey.Should().Be("verifik_simit");
        crudo.SubjectKind.Should().Be(RawProviderPayload.PersonSubject);
        crudo.SubjectKey.Should().Be(Documento);

        // Lo que Trámites usa del resultado no cambia.
        var hoy = VerifikSimitResultMapper.Map(
            System.Text.Json.JsonSerializer.Deserialize<VerifikSimitResponse>(CuerpoPersona, Web)!);
        System.Text.Json.JsonSerializer.Serialize(result with { RawPayload = null }, Web)
            .Should().Be(System.Text.Json.JsonSerializer.Serialize(hoy, Web), "mismo semáforo, chequeos y campos hidratados");
        result.Certifications.Should().BeNull("sin bundle la ingesta canónica de Trámites no se dispara");
    }

    [Fact]
    public async Task VerifikSimit_real_por_placa_guarda_el_cuerpo_con_vehicle_subject()
    {
        var ct = TestContext.Current.CancellationToken;

        var result = await Simit(Responde(HttpStatusCode.OK, CuerpoVehiculo)).ConsultAsync(CtxPlaca(), ct);

        result.RawPayload.Should().NotBeNull();
        var crudo = result.RawPayload!;
        crudo.PayloadJson.Should().Be(CuerpoVehiculo.Trim(), "byte a byte");
        crudo.ProviderKey.Should().Be("verifik_simit");
        crudo.SubjectKind.Should().Be(RawProviderPayload.VehicleSubject);
        crudo.SubjectKey.Should().Be(Placa);
    }

    [Fact]
    public async Task VerifikSimit_404_guarda_el_cuerpo_sin_modificar_el_semaforo_verde()
    {
        // El 404 de Verifik («sin registros») TAMBIÉN guarda el cuerpo (§ T1 del plan):
        // sin el cuerpo del 404 no se puede ver por qué el semáforo quedó verde.
        var ct = TestContext.Current.CancellationToken;

        var result = await Simit(Responde(HttpStatusCode.NotFound, CuerpoVerifik404)).ConsultAsync(CtxPlaca(), ct);

        result.Overall.Should().Be("green", "el 404 de Verifik sigue siendo verde: sin registros");
        result.Checks.Should().ContainSingle(c => c.Status == "ok" && c.Key == "multas");
        result.RawPayload.Should().NotBeNull("el cuerpo del 404 también se guarda");
        var crudo = result.RawPayload!;
        crudo.PayloadJson.Should().Be(CuerpoVerifik404.Trim());
        crudo.ProviderKey.Should().Be("verifik_simit");
        crudo.SubjectKind.Should().Be(RawProviderPayload.VehicleSubject);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task VerifikSimit_sin_respuesta_util_no_hay_crudo(HttpStatusCode estado)
    {
        var ct = TestContext.Current.CancellationToken;
        const string Error = """{"error":"fallo ficticio"}""";

        (await Simit(Responde(estado, Error)).ConsultAsync(CtxPlaca(), ct)).RawPayload
            .Should().BeNull($"simit {estado}: no hay crudo en fallo");
    }

    [Fact]
    public async Task VerifikSimit_mock_no_inventa_crudo()
    {
        var ct = TestContext.Current.CancellationToken;
        var mock = new VerifikSimitConsultationProvider(
            new HttpClient(Responde(HttpStatusCode.OK, "{}")) { BaseAddress = new Uri("https://api.prueba") },
            Options.Create(new VerifikOptions()),
            Options.Create(new ConsultationProviderModeOptions { VerifikSimitMode = "mock" }));

        (await mock.ConsultAsync(CtxDocumento(), ct)).RawPayload.Should().BeNull("el mock de Trámites sigue igual");
    }

    // ── Kyverum Fines ────────────────────────────────────────────────────────────────────────────────

    private const string CuerpoKyverumFines = """
        {"multas":[{"estadoComparendo":"Pendiente","estadoCartera":null,"valorPagar":"604100","numeroComparendo":"C-FICTICIO-01","organismoTransito":"TRANSITO FICTICIO","infracciones":[{"codigoInfraccion":"C02","descripcionInfraccion":"Ficticia"}]}],"acuerdosPago":[],"campoNuevoDelProveedor":"valor desconocido"}
        """;

    [Fact]
    public async Task KyverumFines_real_guarda_el_cuerpo_HTTP_y_no_cambia_semaforo_ni_comparendos()
    {
        var ct = TestContext.Current.CancellationToken;

        var result = await Kyverum(Responde(HttpStatusCode.OK, CuerpoKyverumFines)).ConsultAsync(CtxDocumento(), ct);

        result.RawPayload.Should().NotBeNull();
        var crudo = result.RawPayload!;
        crudo.PayloadJson.Should().Be(CuerpoKyverumFines.Trim(), "byte a byte, incluidas claves desconocidas");
        crudo.ProviderKey.Should().Be("kyverum_fines");
        crudo.SubjectKind.Should().Be(RawProviderPayload.PersonSubject);
        crudo.SubjectKey.Should().Be(Documento);

        result.Overall.Should().Be("yellow", "hay multas pendientes");
        result.Checks.Should().Contain(c => c.Key == FinesCheckFactory.KeyMultas && c.Status == "warn");
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.Unauthorized)]
    public async Task KyverumFines_sin_respuesta_util_no_hay_crudo(HttpStatusCode estado)
    {
        var ct = TestContext.Current.CancellationToken;
        (await Kyverum(Responde(estado, "{}")).ConsultAsync(CtxDocumento(), ct)).RawPayload
            .Should().BeNull($"kyverum {estado}");
    }

    // ── Flit Fines ───────────────────────────────────────────────────────────────────────────────────

    private const string CuerpoFlitFines = """
        {"multas":[{"estadoComparendo":"Pendiente","estadoCartera":null,"valorPagar":"302050","numeroComparendo":"C-FLIT-01","organismoTransito":"TRANSITO FICTICIO","infracciones":[]}],"acuerdosPago":[],"campoAdicional":"extra"}
        """;

    [Fact]
    public async Task FlitFines_real_guarda_el_cuerpo_HTTP_y_no_cambia_semaforo_ni_comparendos()
    {
        var ct = TestContext.Current.CancellationToken;

        var result = await Flit(Responde(HttpStatusCode.OK, CuerpoFlitFines)).ConsultAsync(CtxDocumento(), ct);

        result.RawPayload.Should().NotBeNull();
        var crudo = result.RawPayload!;
        crudo.PayloadJson.Should().Be(CuerpoFlitFines.Trim(), "byte a byte, incluidas claves desconocidas");
        crudo.ProviderKey.Should().Be("flit_fines");
        crudo.SubjectKind.Should().Be(RawProviderPayload.PersonSubject);
        crudo.SubjectKey.Should().Be(Documento);

        result.Overall.Should().Be("yellow", "hay multas pendientes");
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.Unauthorized)]
    public async Task FlitFines_sin_respuesta_util_no_hay_crudo(HttpStatusCode estado)
    {
        var ct = TestContext.Current.CancellationToken;
        (await Flit(Responde(estado, "{}")).ConsultAsync(CtxDocumento(), ct)).RawPayload
            .Should().BeNull($"flit {estado}");
    }

    // ── Apoyo ────────────────────────────────────────────────────────────────────────────────────────

    private static VerifikSimitConsultationProvider Simit(HttpMessageHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("https://api.verifik.test") },
            Options.Create(new VerifikOptions { ApiToken = "t", AuthScheme = "Bearer" }),
            Options.Create(new ConsultationProviderModeOptions { VerifikSimitMode = "real" }));

    private static KyverumFinesConsultationProvider Kyverum(HttpMessageHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("https://runt.kyverum.test") },
            Options.Create(new KyverumFinesOptions { InfractionPath = "/v1/comparendos", ApiKey = "k" }),
            Options.Create(new ConsultationProviderModeOptions { KyverumFinesMode = "real" }));

    private static FlitFinesConsultationProvider Flit(HttpMessageHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("https://api.flit.test") },
            Options.Create(new FlitRegistrationApiOptions()),
            Options.Create(new ConsultationProviderModeOptions { FlitFinesMode = "real" }));

    private static ConsultationContext CtxDocumento() =>
        new(Guid.Empty, Guid.Empty, "prueba",
            new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            {
                ["owner_document_type"] = "CC",
                ["owner_document_number"] = Documento,
            });

    private static ConsultationContext CtxPlaca() =>
        new(Guid.Empty, Guid.Empty, "prueba",
            new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            {
                ["plate"] = Placa,
            });

    private static Respondedor Responde(HttpStatusCode estado, string cuerpo) => new(estado, cuerpo);

    private sealed class Respondedor(HttpStatusCode estado, string cuerpo) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(estado)
            {
                Content = new StringContent(cuerpo.Trim(), Encoding.UTF8, "application/json"),
            });
    }
}
