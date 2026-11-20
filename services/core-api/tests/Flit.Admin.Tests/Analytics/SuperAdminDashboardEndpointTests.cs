using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Flit.Admin.Tests.Companies;
using Flit.Analytics.Application.Queries.SuperAdminDashboard;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace Flit.Admin.Tests.Analytics;

/// <summary>
/// Inicio del SuperAdmin — <c>/api/v1/analytics/superadmin/*</c> es lectura ENTRE TENANTS: solo el SuperAdmin entra
/// (<c>SuperAdminPolicy</c>); el administrador de una compañía, un rol con <c>dashboard.read</c> y el admin de un organismo
/// reciben 403 sin que el repositorio se toque. El repositorio va sustituido: no hace falta PostgreSQL.
/// </summary>
public sealed class SuperAdminDashboardEndpointTests
    : IClassFixture<SuperAdminDashboardEndpointTests.RepoSubstituteFactory>
{
    private const string Companias = "/api/v1/analytics/superadmin/companias-resumen";
    private const string Organismos = "/api/v1/analytics/superadmin/organismos";
    private static readonly Guid Tenant = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private readonly RepoSubstituteFactory _factory;

    public SuperAdminDashboardEndpointTests(RepoSubstituteFactory factory)
    {
        _factory = factory;
        _factory.Repo.ClearReceivedCalls();
        _factory.Repo
            .GetCompaniesAsync(Arg.Any<DateOnly?>(), Arg.Any<DateOnly?>(), Arg.Any<int>(), Arg.Any<IReadOnlyList<string>?>(), Arg.Any<CancellationToken>())
            .Returns([new SuperAdminDashboardCompanyDto(Tenant, "Compañía Uno", 5, 3, 1, 1, 50.0, 2)]);
        _factory.Repo
            .GetOfficesAsync(Arg.Any<Guid?>(), Arg.Any<DateOnly?>(), Arg.Any<DateOnly?>(), Arg.Any<IReadOnlyList<string>?>(), Arg.Any<CancellationToken>())
            .Returns(new SuperAdminDashboardOfficesDto(null, null, null, 0, null, []));
    }

    public static TheoryData<string> NoSuperAdmin() => new()
    {
        TestTokenFactory.CreateAdminCompanyToken(Tenant),
        TestTokenFactory.CreateTokenWithPermissions("Gestor", Tenant, "dashboard.read", "reportes.resumen"),
        TestTokenFactory.CreateOtAdminToken(Tenant),
    };

    [Theory]
    [MemberData(nameof(NoSuperAdmin))]
    public async Task Quien_no_es_SuperAdmin_recibe_403_y_no_se_lee_nada(string token)
    {
        foreach (var url in new[] { Companias, Organismos, $"{Organismos}?tenantId={Tenant}" })
        {
            var response = await Client(token).GetAsync(url, TestContext.Current.CancellationToken);
            response.StatusCode.Should().Be(HttpStatusCode.Forbidden, url);
        }

        _factory.Repo.ReceivedCalls().Should().BeEmpty("el 403 se decide antes del handler");
    }

    [Fact]
    public async Task Sin_token_recibe_401()
    {
        var response = await _factory.CreateClient().GetAsync(Companias, TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task SuperAdmin_sin_rango_lee_el_universo_completo_con_7_dias_de_represado()
    {
        var response = await Client(SuperAdmin()).GetAsync(Companias, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        body.GetProperty("stuckDays").GetInt32().Should().Be(7);
        var fila = body.GetProperty("items")[0];
        fila.GetProperty("nombre").GetString().Should().Be("Compañía Uno");
        fila.GetProperty("tasaAprobacionPct").GetDouble().Should().Be(50.0);
        fila.GetProperty("represados").GetInt32().Should().Be(2);
        await _factory.Repo.Received(1).GetCompaniesAsync(null, null, 7, Arg.Any<IReadOnlyList<string>?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SuperAdmin_con_compania_la_pasa_al_repositorio_de_organismos()
    {
        var response = await Client(SuperAdmin()).GetAsync(
            $"{Organismos}?tenantId={Tenant}&from=2026-11-01&to=2026-11-30", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        await _factory.Repo.Received(1).GetOfficesAsync(
            Tenant, new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 30), Arg.Any<IReadOnlyList<string>?>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(Companias + "?from=2026-11-30&to=2026-11-01")]
    [InlineData(Organismos + "?from=2026-11-30&to=2026-11-01")]
    [InlineData(Companias + "?stuckDays=0")]
    public async Task Parametros_invalidos_dan_400_sin_consultar(string url)
    {
        var response = await Client(SuperAdmin()).GetAsync(url, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        _factory.Repo.ReceivedCalls().Should().BeEmpty();
    }

    private static string SuperAdmin() => TestTokenFactory.CreateToken("SuperAdmin");

    private HttpClient Client(string token)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    /// <summary>Host real con el repositorio del Inicio del SuperAdmin sustituido.</summary>
    public sealed class RepoSubstituteFactory : WebApplicationFactory<Program>
    {
        public ISuperAdminDashboardReadRepository Repo { get; } = Substitute.For<ISuperAdminDashboardReadRepository>();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            ArgumentNullException.ThrowIfNull(builder);
            builder.ConfigureTestServices(services => services.AddScoped(_ => Repo));
        }
    }
}
