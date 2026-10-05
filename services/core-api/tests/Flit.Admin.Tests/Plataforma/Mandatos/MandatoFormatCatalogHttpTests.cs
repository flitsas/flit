using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Flit.Admin.Tests.Plataforma.Mandatos;

/// <summary>
/// HU #13168 (Feature #13118, Épica #13090) — GET /api/v1/admin/plataforma/mandatos/formatos expone el catálogo
/// único de formatos; solo lo lee el Super Admin y no admite crear ni borrar. Reutiliza el arnés HTTP de F5.
/// </summary>
public sealed class MandatoFormatCatalogHttpTests(WebApplicationFactory<Program> factory)
    : CompanyRulesHttpTestBase(factory)
{
    private const string FormatsUrl = "/api/v1/admin/plataforma/mandatos/formatos";

    [Fact]
    public async Task AC1_SuperAdmin_ListaLosCincoFormatos_ConCodigoNombreTipoYRedaccionBase()
    {
        AuthenticateSuperAdmin();

        var response = await Client.GetAsync(FormatsUrl, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var items = (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("items");
        items.EnumerateArray().Select(i => i.GetProperty("code").GetString())
            .Should().Equal("auto", "generico", "sabaneta", "bello", "municipio");
        var sabaneta = items.EnumerateArray().Single(i => i.GetProperty("code").GetString() == "sabaneta");
        sabaneta.GetProperty("name").GetString().Should().Be("Sabaneta");
        sabaneta.GetProperty("assignmentMode").GetString().Should().Be("institutional");
        sabaneta.GetProperty("baseRedaction").GetString().Should().Be("sabaneta");
        sabaneta.GetProperty("selectableAsRedaction").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task AC4_Auto_FiguraComoDelegacion_NoComoRedaccionPrevisualizable()
    {
        AuthenticateSuperAdmin();

        var items = (await (await Client.GetAsync(FormatsUrl, Ct)).Content.ReadFromJsonAsync<JsonElement>(Ct))
            .GetProperty("items");

        var auto = items.EnumerateArray().Single(i => i.GetProperty("code").GetString() == "auto");
        auto.GetProperty("selectableAsRedaction").GetBoolean().Should().BeFalse();
        auto.GetProperty("delegatesToOfficeTemplate").GetBoolean().Should().BeTrue();
        auto.GetProperty("baseRedaction").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task AC3_CodigoDesconocido_EnVistaPrevia_Responde400_ConLosCodigosDelCatalogo()
    {
        AuthenticateSuperAdmin();

        var response = await Client.GetAsync("/api/v1/admin/plataforma/mandatos/inventado/preview", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        body.GetProperty("error").GetString().Should().Be("template_code_invalido");
        body.GetProperty("allowed").EnumerateArray().Select(e => e.GetString())
            .Should().Equal("generico", "sabaneta", "bello", "municipio");
    }

    [Fact]
    public async Task AC3_CodigoDesconocido_AlGuardarElOrganismo_Responde400_ConTodosLosCodigos()
    {
        AuthenticateSuperAdmin();

        var response = await Client.PutAsJsonAsync(
            $"/api/v1/admin/plataforma/mandatos/ot/{Office}",
            new
            {
                templateCode = "inventado", requiresForNaturalPerson = true, mandataryFamily = "individuo",
                assignmentMode = "signer",
            },
            Ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        body.GetProperty("error").GetString().Should().Be("template_code_invalido");
        body.GetProperty("allowed").EnumerateArray().Select(e => e.GetString())
            .Should().Equal("auto", "generico", "sabaneta", "bello", "municipio");
    }

    [Fact]
    public async Task AC3_CodigoDesconocido_EnLaVistaPreviaDelHubOt_Responde400_ConLosCodigosDelCatalogo()
    {
        AuthenticateOtAdmin();

        var response = await Client.GetAsync(
            $"/api/v1/admin/ot/offices/{Office}/mandatos/templates/inventado/preview", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("error").GetString()
            .Should().Be("template_code_invalido");
    }

    [Fact]
    public async Task AC5_SinCrearNiBorrar_PostYDelete_Responden405()
    {
        AuthenticateSuperAdmin();

        var post = await Client.PostAsJsonAsync(FormatsUrl, new { code = "nuevo", name = "Nuevo" }, Ct);
        var delete = await Client.DeleteAsync(FormatsUrl, Ct);

        post.StatusCode.Should().Be(HttpStatusCode.MethodNotAllowed);
        delete.StatusCode.Should().Be(HttpStatusCode.MethodNotAllowed);
        var after = (await (await Client.GetAsync(FormatsUrl, Ct)).Content.ReadFromJsonAsync<JsonElement>(Ct))
            .GetProperty("items");
        after.GetArrayLength().Should().Be(5);
    }

    [Fact]
    public async Task AC6_AdminOt_403_AdminDeCompania_403_SinToken_401()
    {
        AuthenticateOtAdmin();
        var ot = await Client.GetAsync(FormatsUrl, Ct);
        AuthenticateCompanyAdmin();
        var company = await Client.GetAsync(FormatsUrl, Ct);
        AuthenticateAnonymous();
        var anonymous = await Client.GetAsync(FormatsUrl, Ct);

        ot.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        company.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        anonymous.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
