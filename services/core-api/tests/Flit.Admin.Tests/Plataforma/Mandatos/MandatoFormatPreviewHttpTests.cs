using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Flit.Admin.Tests.Plataforma.Mandatos;

/// <summary>
/// HU #13173 (Feature #13118, Épica #13090) — vista previa de formatos: <c>auto</c> con organismo y borrador de
/// plantilla (POST /mandatos/formatos/{code}/preview). Solo el Super Admin; el hub OT conserva su vista previa.
/// </summary>
public sealed class MandatoFormatPreviewHttpTests(WebApplicationFactory<Program> factory)
    : CompanyRulesHttpTestBase(factory)
{
    private const string Base = "/api/v1/admin/plataforma/mandatos";

    private static void ShouldBePdf(byte[] bytes) =>
        Encoding.ASCII.GetString(bytes, 0, 4).Should().Be("%PDF");

    private Task<HttpResponseMessage> DraftAsync(string code, string? body) =>
        Client.PostAsJsonAsync($"{Base}/formatos/{code}/preview", new { body }, Ct);

    [Fact]
    public async Task AC1_Auto_ConOrganismo_Responde200ConElPdfDeLaRedaccionEfectiva()
    {
        AuthenticateSuperAdmin();

        var response = await Client.GetAsync($"{Base}/auto/preview?officeId={Office}", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/pdf");
        ShouldBePdf(await response.Content.ReadAsByteArrayAsync(Ct));
    }

    [Fact]
    public async Task AC2_Auto_SinOrganismo_Responde400_OrganismoRequerido_ConElMensajeDeComoPrevisualizar()
    {
        AuthenticateSuperAdmin();

        var response = await Client.GetAsync($"{Base}/auto/preview", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        body.GetProperty("error").GetString().Should().Be("organismo_requerido");
        body.GetProperty("message").GetString().Should().Contain("officeId");
    }

    [Fact]
    public async Task AC2_Auto_ConOrganismoInexistente_Responde404()
    {
        AuthenticateSuperAdmin();

        var response = await Client.GetAsync($"{Base}/auto/preview?officeId={Guid.NewGuid()}", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task AC3_BorradorValido_Responde200ConElPdfDeMuestra_YNoGuardaVersionAlguna()
    {
        AuthenticateSuperAdmin();
        await using var before = CreateDbContext();
        var versionsBefore = await before.MandateFormatVersions.CountAsync(Ct);

        var response = await DraftAsync("bello", "Contrato de mandato sobre {{placa}} otorgado por {{mandante_nombre}}.");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        ShouldBePdf(await response.Content.ReadAsByteArrayAsync(Ct));
        await using var after = CreateDbContext();
        (await after.MandateFormatVersions.CountAsync(Ct)).Should().Be(versionsBefore);
    }

    [Fact]
    public async Task AC4_BorradorConVariableDesconocida_Responde400_PlantillaVariableInvalida_ConPosicion()
    {
        AuthenticateSuperAdmin();

        var response = await DraftAsync("generico", "Linea uno\nCédula {{cedula_inventada}}");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        body.GetProperty("error").GetString().Should().Be("plantilla_variable_invalida");
        var unknown = body.GetProperty("unknownVariables").EnumerateArray().Single();
        unknown.GetProperty("name").GetString().Should().Be("cedula_inventada");
        unknown.GetProperty("line").GetInt32().Should().Be(2);
        body.GetProperty("allowedVariables").EnumerateArray().Select(e => e.GetString()).Should().Contain("placa");
    }

    [Theory]
    [InlineData("", "plantilla_vacia")]
    [InlineData("Placa {{placa", "plantilla_sintaxis_invalida")]
    public async Task AC4_BorradorVacioOConLlavesSinCerrar_Responde400ConElCodigoDelValidador(string body, string error)
    {
        AuthenticateSuperAdmin();

        var response = await DraftAsync("bello", body);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("error").GetString().Should().Be(error);
    }

    [Fact]
    public async Task AC4_BorradorDeMasDe100000Caracteres_Responde400()
    {
        AuthenticateSuperAdmin();

        var response = await DraftAsync("bello", new string('a', 100_001));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("error").GetString()
            .Should().Be("plantilla_demasiado_larga");
    }

    [Theory]
    [InlineData("inventado")]
    [InlineData("auto")]
    public async Task Borrador_ConCodigoFueraDeLasRedacciones_Responde400_TemplateCodeInvalido(string code)
    {
        AuthenticateSuperAdmin();

        var response = await DraftAsync(code, "Hola {{placa}}");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("error").GetString()
            .Should().Be("template_code_invalido");
    }

    [Theory]
    [InlineData("generico")]
    [InlineData("sabaneta")]
    [InlineData("bello")]
    [InlineData("municipio")]
    public async Task AC6_SinRegresion_LosCuatroCodigosSiguenRespondiendo200(string code)
    {
        AuthenticateSuperAdmin();

        var response = await Client.GetAsync($"{Base}/{code}/preview", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        ShouldBePdf(await response.Content.ReadAsByteArrayAsync(Ct));
    }

    [Fact]
    public async Task AC7_Acceso_AdminOtYAdminDeCompania_403_SinToken_401_ElHubOtConservaSuVistaPrevia()
    {
        AuthenticateOtAdmin();
        var otDraft = await DraftAsync("bello", "Hola {{placa}}");
        var otAuto = await Client.GetAsync($"{Base}/auto/preview?officeId={Office}", Ct);
        var otOwn = await Client.GetAsync($"/api/v1/admin/ot/offices/{Office}/mandatos/templates/bello/preview", Ct);
        AuthenticateCompanyAdmin();
        var companyDraft = await DraftAsync("bello", "Hola {{placa}}");
        AuthenticateAnonymous();
        var anonymousDraft = await DraftAsync("bello", "Hola {{placa}}");

        otDraft.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        otAuto.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        companyDraft.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        anonymousDraft.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        otOwn.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
