using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using Flit.Admin.Domain.OtMetrics;
using Flit.Modules.Platform.Domain.Access;
using Flit.Queries.Domain.Tenancy;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using NSubstitute;
using Xunit;

namespace Flit.Admin.Tests.OtMetrics;

/// <summary>
/// Rediseño del Inicio (2026-11-17) — HTTP de <c>GET /api/v1/admin/ot/metrics/clientes-por-familia</c>: va bajo la
/// policy del módulo OT como el resto de <c>/admin/ot/metrics/*</c>. Host real; el repositorio se sustituye para no
/// depender de PostgreSQL (la lectura en sí la cubre <see cref="OtInicioMetricsTests"/>).
/// </summary>
public sealed class OtClientsByFamilyEndpointTests : IClassFixture<OtClientsByFamilyEndpointTests.Factory>
{
    private const string Ruta = "/api/v1/admin/ot/metrics/clientes-por-familia";
    private static readonly Guid TenantOt = Guid.Parse("0e000000-0000-4000-8000-00000001117a");
    private static readonly Guid TenantEmpresa = Guid.Parse("0e000000-0000-4000-8000-00000001117b");

    private readonly Factory _factory;

    public OtClientsByFamilyEndpointTests(Factory factory)
    {
        _factory = factory;
        _factory.Repo.ClearReceivedCalls();
        _factory.Repo
            .GetClientsByFamilyAsync(Arg.Any<Guid>(), Arg.Any<OtMetricsFilter>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(new OtClientsByFamilyDto(
                [new OtClientFamilyRowDto(Guid.NewGuid(), "Flota Andina S.A.S.", 3, 1, 1, 5)], 5));
    }

    [Fact]
    public async Task UsuarioDelOrganismo_200_ConLaTablaYSinFiltrosDeFamiliaNiEmpresa()
    {
        var ct = TestContext.Current.CancellationToken;
        // family y clientTenantId viajan en la URL a propósito: el endpoint los descarta (la tabla ES ese cruce).
        var response = await Cliente(Token("ot_admin", TenantOt, otEntity: true))
            .GetAsync($"{Ruta}?from=2026-11-01&to=2026-11-17&family=TRASPASO&clientTenantId={Guid.NewGuid()}", ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<OtClientsByFamilyDto>(ct);
        body!.Total.Should().Be(5);
        body.Clientes.Should().ContainSingle().Which.Matriculas.Should().Be(3);

        await _factory.Repo.Received(1).GetClientsByFamilyAsync(
            TenantOt,
            Arg.Is<OtMetricsFilter>(f => f.From == new DateOnly(2026, 11, 1) && f.To == new DateOnly(2026, 11, 17)
                && f.Familia == null && f.ClientTenantId == null),
            null,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UsuarioDeEmpresa_NoOt_403_SinTocarElRepositorio()
    {
        var ct = TestContext.Current.CancellationToken;
        var response = await Cliente(Token("gestor", TenantEmpresa, otEntity: false))
            .GetAsync($"{Ruta}?from=2026-11-01&to=2026-11-17", ct);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        await _factory.Repo.DidNotReceiveWithAnyArgs().GetClientsByFamilyAsync(default, default!, default, default);
    }

    [Fact]
    public async Task SinToken_401()
    {
        var response = await _factory.CreateClient()
            .GetAsync($"{Ruta}?from=2026-11-01&to=2026-11-17", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task SinRango_400()
    {
        var response = await Cliente(Token("ot_admin", TenantOt, otEntity: true))
            .GetAsync(Ruta, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    private HttpClient Cliente(string token)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static string Token(string role, Guid tenantId, bool otEntity)
    {
        var claims = new List<Claim>
        {
            new("sub", "0e000000-0000-4000-8000-000000000001"),
            new("role", role),
            new("role_code", role),
            new("tenant_id", tenantId.ToString()),
            new("permissions", "ot.reportes.manage"),
        };
        if (otEntity)
        {
            claims.Add(new Claim("entity_type", "TRANSIT_OFFICE"));
        }

        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = "https://api.flit.co",
            Audience = "flit-api",
            Subject = new ClaimsIdentity(claims),
            Expires = DateTime.UtcNow.AddHours(1),
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(new string('k', 64))), SecurityAlgorithms.HmacSha256),
        });
    }

    /// <summary>Host real con el repositorio de métricas OT sustituido.</summary>
    public sealed class Factory : WebApplicationFactory<Program>
    {
        public IOtMetricsReadRepository Repo { get; } = Substitute.For<IOtMetricsReadRepository>();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            ArgumentNullException.ThrowIfNull(builder);
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IOtMetricsReadRepository>();
                services.AddScoped(_ => Repo);
                // El middleware de alcance del tenant consulta PostgreSQL; aquí basta con el tenant del token.
                services.AddScoped<ITenantScopeResolver>(_ => new AlcanceUnico());
                // El middleware de producto también consulta la BD: se responde «producto encendido» para el tenant.
                var productos = Substitute.For<IProductAccessStore>();
                productos.GetTenantChainAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
                    .Returns(call => (IReadOnlyList<Guid>)[call.ArgAt<Guid>(0)]);
                productos.GetTenantsWithProductEnabledAsync(Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                    .Returns(call => (IReadOnlySet<Guid>)call.ArgAt<IReadOnlyList<Guid>>(0).ToHashSet());
                services.AddScoped(_ => productos);
            });
        }
    }

    private sealed class AlcanceUnico : ITenantScopeResolver
    {
        public Task<TenantScope> ResolveAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
            Task.FromResult(TenantScope.Single(tenantId));
    }
}
