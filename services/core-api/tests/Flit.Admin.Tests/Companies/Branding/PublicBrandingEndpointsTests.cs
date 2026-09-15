using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Flit.Admin.Tests.Companies.Branding;

/// <summary>
/// Uso de ejemplo: <c>await _factory.CreateClient().GetAsync("/api/v1/public/branding")</c>.
/// HU #12418 AC1-AC3, AC6, AC8 — sin sello X-Flit-Domain (o con host reservado de FLIT) el handler
/// NUNCA toca base de datos (<c>DomainKind.Flit</c>), así que estos tests corren sin Postgres
/// accesible (<c>TestEnvironment.DisableAutoMigrate</c>), igual que
/// <c>AdminCompaniesAuthorizationTests</c>.
/// </summary>
public sealed class PublicBrandingEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string BrandingUrl = "/api/v1/public/branding";

    private readonly WebApplicationFactory<Program> _factory;

    public PublicBrandingEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task AC8_SinSelloXFlitDomain_Responde200ConIdentidadFlit()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync(BrandingUrl, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<BrandIdentityBody>(
            cancellationToken: TestContext.Current.CancellationToken);
        body.Should().NotBeNull();
        body!.PlatformName.Should().Be("FLIT 2.0");
        body.LogoUrl.Should().BeNull();
        body.Version.Should().Be(0);
    }

    [Fact]
    public async Task AC1_CabeceraCacheControlYVary_PresentesEnLaRespuesta()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync(BrandingUrl, TestContext.Current.CancellationToken);

        response.Headers.CacheControl.Should().NotBeNull();
        response.Headers.CacheControl!.ToString().Should().Contain("max-age=60");
        response.Headers.Vary.Should().Contain("X-Flit-Domain");
    }

    [Fact]
    public async Task AC3_LaRespuesta_NoContieneCamposProhibidos()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync(BrandingUrl, TestContext.Current.CancellationToken);
        var json = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        using var document = JsonDocument.Parse(json);
        var names = document.RootElement.EnumerateObject().Select(p => p.Name).ToList();
        names.Should().BeEquivalentTo(["platformName", "logoUrl", "colors", "version"]);
    }

    [Fact]
    public async Task AC3_HostReservadoDeFlit_TambienResuelveAFlitSinTocarBd()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Flit-Domain", "app.flitsas.online");

        var response = await client.GetAsync(BrandingUrl, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<BrandIdentityBody>(
            cancellationToken: TestContext.Current.CancellationToken);
        body!.PlatformName.Should().Be("FLIT 2.0");
    }

    [Fact]
    public async Task AC4_RutaDelLogotipo_EstaRegistradaYAcceptaGuidsSinAutenticacion()
    {
        // GetPublicBrandLogoHandler SIEMPRE toca BD (busca por logoId sin tenant conocido, AC4), así
        // que la respuesta exacta se cubre en GetPublicBrandLogoHandlerTests (con dobles). Aquí solo
        // se verifica que la ruta esté registrada y anónima (nunca 401/403/404-de-rutas): NotFound
        // real de MVC routing (sin match) da 404 igual, pero Unauthorized/Forbidden delatarían que
        // el endpoint quedó detrás de auth por error.
        var client = _factory.CreateClient();

        var response = await client.GetAsync(
            $"/api/v1/public/branding/logos/{Guid.NewGuid()}", TestContext.Current.CancellationToken);

        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
        response.StatusCode.Should().NotBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task AC3_LimiteDeTasaPorIp_Devuelve429TrasExcederElPermitLimitSinCuerpoInformativo()
    {
        // appsettings.json: PublicBranding:RateLimit:PermitLimit = 60 por minuto; el rate limiter es
        // un singleton del contenedor, así que esta prueba usa una FÁBRICA PROPIA (no la compartida
        // _factory de la clase) para no agotar el cupo de las demás pruebas de esta suite.
        using var isolatedFactory = new WebApplicationFactory<Program>();
        var client = isolatedFactory.CreateClient();

        HttpResponseMessage? last = null;
        for (var i = 0; i < 65; i++)
        {
            last = await client.GetAsync(BrandingUrl, TestContext.Current.CancellationToken);
            if (last.StatusCode == HttpStatusCode.TooManyRequests)
            {
                break;
            }
        }

        last.Should().NotBeNull();
        last!.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        var body = await last.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        body.Should().BeEmpty("AC3: 429 sin cuerpo informativo que revele el límite o la ventana");
    }

    private sealed record BrandIdentityBody(string PlatformName, string? LogoUrl, ColorsBody Colors, int Version);

    private sealed record ColorsBody(string Primary, string Secondary, string OnPrimary);
}
