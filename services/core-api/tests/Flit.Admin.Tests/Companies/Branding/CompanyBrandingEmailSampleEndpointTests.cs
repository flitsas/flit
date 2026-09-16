using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Flit.Admin.Domain.Companies;
using Flit.Admin.Domain.Companies.Branding;
using Flit.Admin.Tests.Companies;
using Flit.Api.Middleware;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace Flit.Admin.Tests.Companies.Branding;

/// <summary>
/// HU #12428 (endpoint nuevo), HU #12431 AC1, HU #12414 AC4 — <c>GET /company/branding/email-sample</c>.
/// Mismo patrón que <see cref="Flit.Admin.Tests.Notifications.AdminPlataformaNotificacionesPlantillasEndpointsTests"/>:
/// <c>WebApplicationFactory&lt;Program&gt;</c> con dobles de NSubstitute para no tocar PostgreSQL.
/// </summary>
public sealed class CompanyBrandingEmailSampleEndpointTests : IClassFixture<CompanyBrandingEmailSampleEndpointTests.Factory>
{
    private const string Url = "/api/v1/company/branding/email-sample";
    private static readonly Guid HeadTenantId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly BrandColors Colors = new("#0B3D91", "#1FA2FF", "#FFFFFF");

    private readonly Factory _factory;

    public CompanyBrandingEmailSampleEndpointTests(Factory factory)
    {
        _factory = factory;
        _factory.Hierarchy.ClearReceivedCalls();
        _factory.BrandingRepository.ClearReceivedCalls();
    }

    [Fact]
    public async Task Published_ConMarcaPublicada_DevuelveThemeBrand()
    {
        _factory.Hierarchy.GetHierarchyInfoAsync(HeadTenantId, Arg.Any<CancellationToken>())
            .Returns(new CompanyHierarchyInfo(HeadTenantId, "MARCA_BLANCA", true, null));
        var draft = new BrandingDraft("Movilidad Andina", Colors, Guid.NewGuid());
        _factory.BrandingRepository.GetByTenantIdAsync(HeadTenantId, Arg.Any<CancellationToken>())
            .Returns(new TenantBranding { TenantId = HeadTenantId, Draft = draft, Published = draft, PublishedVersion = 4, RowVersion = 1 });

        var response = await HeadClient().GetAsync(Url, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<SampleDto>(TestContext.Current.CancellationToken);
        body!.Theme!.Kind.Should().Be("brand");
        body.Theme.PlatformName.Should().Be("Movilidad Andina");
        body.Theme.Version.Should().Be(4);
    }

    [Fact]
    public async Task Published_SinMarcaPublicada_Devuelve200ConThemeFlit_NuncaNotFound()
    {
        _factory.Hierarchy.GetHierarchyInfoAsync(HeadTenantId, Arg.Any<CancellationToken>())
            .Returns(new CompanyHierarchyInfo(HeadTenantId, "MARCA_BLANCA", true, null));
        _factory.BrandingRepository.GetByTenantIdAsync(HeadTenantId, Arg.Any<CancellationToken>())
            .Returns((TenantBranding?)null);

        var response = await HeadClient().GetAsync(Url, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<SampleDto>(TestContext.Current.CancellationToken);
        body!.Theme!.Kind.Should().Be("flit");
    }

    [Fact]
    public async Task Draft_ConBorradorIncompleto_DevuelveThemeDraftPartial()
    {
        _factory.Hierarchy.GetHierarchyInfoAsync(HeadTenantId, Arg.Any<CancellationToken>())
            .Returns(new CompanyHierarchyInfo(HeadTenantId, "MARCA_BLANCA", true, null));
        var incompleteDraft = new BrandingDraft("Movilidad Andina", null, null);
        _factory.BrandingRepository.GetByTenantIdAsync(HeadTenantId, Arg.Any<CancellationToken>())
            .Returns(new TenantBranding { TenantId = HeadTenantId, Draft = incompleteDraft, Published = null, RowVersion = 1 });

        var response = await HeadClient().GetAsync($"{Url}?source=draft", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<SampleDto>(TestContext.Current.CancellationToken);
        body!.Theme!.Kind.Should().Be("draft-partial");
    }

    [Fact]
    public async Task TemplateIdFueraDeLista_Devuelve400BrandingSampleTemplateNotAllowed()
    {
        _factory.Hierarchy.GetHierarchyInfoAsync(HeadTenantId, Arg.Any<CancellationToken>())
            .Returns(new CompanyHierarchyInfo(HeadTenantId, "MARCA_BLANCA", true, null));

        var response = await HeadClient().GetAsync($"{Url}?templateId=security.invitation", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var json = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        json.Should().Contain(BrandingErrors.SampleTemplateNotAllowed);
    }

    [Fact]
    public async Task Hija_Devuelve403()
    {
        var childTenantId = Guid.NewGuid();
        _factory.Hierarchy.GetHierarchyInfoAsync(childTenantId, Arg.Any<CancellationToken>())
            .Returns(new CompanyHierarchyInfo(childTenantId, "RENTING", false, HeadTenantId));

        var response = await ClientFor(childTenantId).GetAsync(Url, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    /// <summary>
    /// La policy <see cref="Flit.Api.Authorization.GroupHeadCompanyAuthorizationHandler"/> exige
    /// <c>is_group_parent = true</c> (HU #12345 AC4) sin filtrar por <c>tenant_type</c> — una
    /// Concesión sin hijas (el caso real y común) tiene <c>is_group_parent = false</c> y cae por el
    /// mismo camino que una hija cualquiera. Si alguna vez existe una Concesión CON hijas
    /// (<c>is_group_parent = true</c>), la policy la deja pasar igual que a una cabeza MARCA_BLANCA
    /// — no es un caso que este endpoint distinga hoy (mismo comportamiento que
    /// <c>GET /company/branding</c>, que tampoco filtra por <c>tenant_type</c>); a confirmar si
    /// #12429 exige lo contrario.
    /// </summary>
    [Fact]
    public async Task Concesion_SinHijas_Devuelve403()
    {
        var concesionId = Guid.NewGuid();
        _factory.Hierarchy.GetHierarchyInfoAsync(concesionId, Arg.Any<CancellationToken>())
            .Returns(new CompanyHierarchyInfo(concesionId, "CONCESION", false, null));

        var response = await ClientFor(concesionId).GetAsync(Url, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public void RutaCubiertaPorRuntimeScopedRoutes()
    {
        TenantEnforcementMiddleware.RuntimeScopedRoutes
            .Any(r => r.Matches(Url))
            .Should().BeTrue($"{Url} debe quedar cubierta por la declaración Prefix de /api/v1/company/branding");
    }

    private HttpClient HeadClient() => ClientFor(HeadTenantId);

    private HttpClient ClientFor(Guid tenantId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestTokenFactory.CreateAdminCompanyToken(tenantId));
        return client;
    }

    private sealed record EmailThemeInfoDto(string Kind, string PlatformName, int? Version, string? SenderName);

    private sealed record SampleDto(string TemplateId, string Subject, string Html, EmailThemeInfoDto? Theme);

    public sealed class Factory : WebApplicationFactory<Program>
    {
        public ICompanyHierarchyRepository Hierarchy { get; } = Substitute.For<ICompanyHierarchyRepository>();
        public ITenantBrandingRepository BrandingRepository { get; } = Substitute.For<ITenantBrandingRepository>();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            ArgumentNullException.ThrowIfNull(builder);

            builder.ConfigureTestServices(services =>
            {
                services.AddScoped(_ => Hierarchy);
                services.AddScoped(_ => BrandingRepository);
            });
        }
    }
}
