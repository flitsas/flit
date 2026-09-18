using System.Net;
using System.Net.Http.Headers;
using Flit.Admin.Domain.Companies;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace Flit.Admin.Tests.Companies;

/// <summary>
/// HU #12710 (Epic #12685) — el Administrador de Compañía, sea sin red, de una hija o de una cabeza de
/// red, solo conserva Representantes legales y Mandatarios en Administración; el resto de secciones
/// responde 403 en la API aun sobre su propio tenant. Solo el SuperAdmin conserva su acceso.
/// <list type="bullet">
///   <item>AC4 — secciones reservadas: 403 para el AdminCompany sin red, el de una hija y el de una cabeza.</item>
///   <item>AC3 — Representantes legales y Mandatarios: siguen abiertos para ese mismo rol.</item>
///   <item>AC6 — la cabeza tampoco opera la configuración de sus hijas; conserva sus Representantes y
///   Mandatarios y el Panel de red (listado, invitaciones y usuarios).</item>
///   <item>AC7 — el SuperAdmin opera todas las rutas como hoy.</item>
/// </list>
/// La jerarquía sale de un <see cref="ICompanyHierarchyRepository"/> falso (misma fuente que usa
/// <c>GroupHeadCompanyAuthorizationHandler</c>), así la decisión no depende de la base local.
/// </summary>
public sealed class AdminCompanyReservedSectionsTests
    : IClassFixture<AdminCompanyReservedSectionsTests.HierarchyFactory>
{
    private static readonly Guid SinRed = Guid.Parse("e0000000-0000-4000-8000-00000000005a");
    private static readonly Guid Cabeza = Guid.Parse("e0000000-0000-4000-8000-000000000100");
    private static readonly Guid Hija = Guid.Parse("e0000000-0000-4000-8000-000000001100");
    private static readonly Guid Oficina = Guid.Parse("e0000000-0000-4000-8000-0000000000f1");

    /// <summary>Rutas de las secciones reservadas, relativas a <c>/api/v1/admin/companies/{tenantId}</c>.</summary>
    public static TheoryData<string, string> Reservadas => new()
    {
        { "GET", "/settings" },
        { "PUT", "/settings" },
        { "GET", "/whitelist" },
        { "POST", "/whitelist" },
        { "GET", "/transit-grants" },
        { "POST", "/transit-grants" },
        { "DELETE", $"/transit-grants/{Oficina}" },
        { "GET", "/transit-agreements" },
        { "PUT", $"/transit-agreements/{Oficina}" },
        { "GET", "/audit-log" },
        { "GET", "/ot-consultation-restrictions" },
        { "PUT", $"/ot-consultation-restrictions/{Oficina}/rnmc" },
        { "GET", "/ot-blocking-policies" },
        { "PUT", $"/ot-blocking-policies/{Oficina}/soat" },
        { "GET", "/ot-prenda-document-policies" },
        { "PUT", $"/ot-prenda-document-policies/{Oficina}" },
        { "GET", "/document-params" },
        { "PUT", "/document-params" },
        { "GET", "/personalized-documents" },
        { "POST", "/personalized-documents" },
        { "GET", "/notification-delivery-logs" },
    };

    /// <summary>
    /// Secciones reservadas de una hija vistas por la cabeza, relativas a
    /// <c>/api/v1/admin/companies/{cabeza}/children/{hija}</c>.
    /// </summary>
    public static TheoryData<string, string> ReservadasDeHija => new()
    {
        { "GET", "/settings" },
        { "PUT", "/settings" },
        { "GET", "/whitelist" },
        { "POST", "/whitelist" },
        { "GET", "/audit-log" },
        { "GET", "/document-params" },
        { "GET", "/personalized-documents" },
    };

    /// <summary>Lo que la cabeza conserva sobre una hija: sus Representantes y Mandatarios y el Panel de red.</summary>
    public static TheoryData<string> ConservadasDeHija => new()
    {
        $"/children/{Hija}/legal-representatives",
        $"/children/{Hija}/mandate-signers",
        "/children",
        $"/children/{Hija}",
        $"/children/{Hija}/users",
    };

    /// <summary>Secciones que el Administrador de Compañía conserva (AC3).</summary>
    public static TheoryData<string> Conservadas => new()
    {
        "/legal-representatives",
        "/mandate-signers",
    };

    private readonly HierarchyFactory _factory;

    public AdminCompanyReservedSectionsTests(HierarchyFactory factory) => _factory = factory;

    // ── AC4 — secciones reservadas ─────────────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(Reservadas))]
    public async Task AdminCompany_sin_red_recibe_403_en_las_secciones_reservadas_de_su_compania(string method, string ruta)
    {
        var response = await SendAsync(TestTokenFactory.CreateAdminCompanyToken(SinRed), method, SinRed, ruta);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Theory]
    [MemberData(nameof(Reservadas))]
    public async Task AdminCompany_de_una_hija_recibe_403_en_las_secciones_reservadas_de_su_compania(string method, string ruta)
    {
        var response = await SendAsync(TestTokenFactory.CreateAdminCompanyToken(Hija), method, Hija, ruta);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ── AC3 — secciones que conserva ───────────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(Conservadas))]
    public async Task AdminCompany_sin_red_conserva_representantes_y_mandatarios(string ruta)
    {
        var response = await SendAsync(TestTokenFactory.CreateAdminCompanyToken(SinRed), "GET", SinRed, ruta);

        response.StatusCode.Should().NotBe(HttpStatusCode.Forbidden);
        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AdminCompany_sin_red_sigue_leyendo_la_ficha_de_su_compania()
    {
        // La cabecera de la ficha (nombre, NIT) no es una sección: la página la necesita para abrir.
        var response = await SendAsync(TestTokenFactory.CreateAdminCompanyToken(SinRed), "GET", SinRed, "");

        response.StatusCode.Should().NotBe(HttpStatusCode.Forbidden);
    }

    // ── AC4 / AC6 — la cabeza de red, igual que cualquier Administrador de Compañía ────────────

    [Theory]
    [MemberData(nameof(Reservadas))]
    public async Task Cabeza_de_red_recibe_403_en_las_secciones_reservadas_de_su_compania(string method, string ruta)
    {
        var response = await SendAsync(TestTokenFactory.CreateAdminCompanyToken(Cabeza), method, Cabeza, ruta);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Theory]
    [MemberData(nameof(ReservadasDeHija))]
    public async Task Cabeza_de_red_recibe_403_en_la_configuracion_de_sus_hijas(string method, string ruta)
    {
        var response = await SendAsync(
            TestTokenFactory.CreateAdminCompanyToken(Cabeza), method, Cabeza, $"/children/{Hija}{ruta}");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Theory]
    [MemberData(nameof(ConservadasDeHija))]
    public async Task Cabeza_de_red_conserva_representantes_mandatarios_y_panel_de_red_de_sus_hijas(string ruta)
    {
        var response = await SendAsync(TestTokenFactory.CreateAdminCompanyToken(Cabeza), "GET", Cabeza, ruta);

        response.StatusCode.Should().NotBe(HttpStatusCode.Forbidden);
        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [MemberData(nameof(Conservadas))]
    public async Task Cabeza_de_red_conserva_representantes_y_mandatarios_de_su_compania(string ruta)
    {
        var response = await SendAsync(TestTokenFactory.CreateAdminCompanyToken(Cabeza), "GET", Cabeza, ruta);

        response.StatusCode.Should().NotBe(HttpStatusCode.Forbidden);
        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
    }

    // ── AC7 — SuperAdmin sin cambios ───────────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(Reservadas))]
    public async Task SuperAdmin_sigue_operando_las_secciones_reservadas(string method, string ruta)
    {
        var response = await SendAsync(TestTokenFactory.CreateToken("SuperAdmin"), method, SinRed, ruta);

        response.StatusCode.Should().NotBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task La_cabeza_no_opera_las_secciones_reservadas_de_otra_compania_por_esta_ruta()
    {
        // Sus hijas se administran por /admin/companies/{head}/children/** (sin cambios); por la ruta
        // directa sigue mandando CompanyOwnTenantFilter.
        var response = await SendAsync(TestTokenFactory.CreateAdminCompanyToken(Cabeza), "GET", Hija, "/settings");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private async Task<HttpResponseMessage> SendAsync(string token, string method, Guid tenantId, string ruta)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var request = new HttpRequestMessage(new HttpMethod(method), $"/api/v1/admin/companies/{tenantId}{ruta}");
        if (method is "POST" or "PUT")
            request.Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");
        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    public sealed class HierarchyFactory : WebApplicationFactory<Program>
    {
        private readonly ICompanyHierarchyRepository _hierarchy = Substitute.For<ICompanyHierarchyRepository>();

        public HierarchyFactory()
        {
            _hierarchy.GetHierarchyInfoAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
                .Returns(call => call.ArgAt<Guid>(0) switch
                {
                    var id when id == Cabeza => new CompanyHierarchyInfo(Cabeza, "MARCA_BLANCA", true, null),
                    var id when id == Hija => new CompanyHierarchyInfo(Hija, "CONCESIONARIO", false, Cabeza),
                    var id => new CompanyHierarchyInfo(id, "CONCESIONARIO", false, null),
                });
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            ArgumentNullException.ThrowIfNull(builder);
            builder.ConfigureTestServices(services => services.AddScoped(_ => _hierarchy));
        }
    }
}
