using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Flit.Api.Authorization;
using Flit.Queries.Domain.Tenancy;
using Flit.Tramites.Domain.Repositories;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using NSubstitute;
using Xunit;

namespace Flit.Admin.Tests.Tramites;

/// <summary>
/// HU #13296 (Feature #13282, Épica #13202) — <c>GET /api/v1/tramites/biometric-validations/manual</c> con el
/// pipeline HTTP real (middleware de tenant + filtro de Super Admin). El repositorio es un doble que SOLO devuelve
/// filas si se le llama: así, un 403/401 que no lo consulta demuestra que la guarda corre antes que la lectura.
/// La lectura cross-tenant real contra PostgreSQL se prueba en <c>ManualIdentityReviewReadRepositoryTests</c>.
/// </summary>
public sealed class ManualIdentityReviewEndpointsTests : IClassFixture<ManualIdentityReviewEndpointsTests.ManualFactory>
{
    private const string Url = "/api/v1/tramites/biometric-validations/manual";

    private static readonly Guid TenantA = Guid.Parse("c1000000-0000-4000-8000-0000000000aa");
    private static readonly Guid TenantB = Guid.Parse("c2000000-0000-4000-8000-0000000000bb");
    private static readonly Guid UserId = Guid.Parse("44444444-4444-4444-4444-444444444444");

    private readonly ManualFactory _factory;

    public ManualIdentityReviewEndpointsTests(ManualFactory factory)
    {
        _factory = factory;
        _factory.Repo.ClearReceivedCalls();
    }

    [Fact]
    public async Task SuperAdmin_recibe_200_con_filas_de_dos_companias_y_el_tiempo_en_espera()
    {
        var response = await ClientFor("SuperAdmin", TenantA).GetAsync(Url, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var body = await JsonAsync(response);
        var items = body.RootElement.GetProperty("items").EnumerateArray().ToList();
        items.Select(i => i.GetProperty("tenantName").GetString()).Should().BeEquivalentTo(["Compañía A", "Compañía B"]);
        body.RootElement.GetProperty("total").GetInt32().Should().Be(2);
        body.RootElement.GetProperty("page").GetInt32().Should().Be(1);
        body.RootElement.GetProperty("pageSize").GetInt32().Should().Be(20);

        var pendiente = items.Single(i => i.GetProperty("status").GetString() == "pendiente_revision_manual");
        pendiente.GetProperty("waitingMinutes").GetInt32().Should().BeGreaterThanOrEqualTo(59);
        pendiente.GetProperty("origin").GetString().Should().Be("tramite");
        var cerrada = items.Single(i => i.GetProperty("status").GetString() == "rechazado");
        cerrada.GetProperty("waitingMinutes").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task SuperAdmin_sin_compania_en_el_token_tambien_lee_todas()
    {
        var response = await ClientFor("SuperAdmin", tenant: null).GetAsync(Url, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData("AdminCompany")]
    [InlineData("AdminOT")]
    [InlineData("Radicador")]
    public async Task Los_demas_roles_reciben_403_sin_consultar(string role)
    {
        var response = await ClientFor(role, TenantA, ["validaciones.read", "validaciones.manage", "dashboard.read"])
            .GetAsync(Url, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        using var body = await JsonAsync(response);
        body.RootElement.GetProperty("code").GetString().Should().Be("super_admin_required");
        _factory.Repo.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task Anonimo_recibe_401_sin_consultar()
    {
        var response = await _factory.CreateClient().GetAsync(Url, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        _factory.Repo.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task Filtros_y_paginacion_llegan_saneados_al_repositorio()
    {
        var response = await ClientFor("SuperAdmin", null).GetAsync(
            $"{Url}?status=PENDIENTE_REVISION_MANUAL&origin=mandatario&q=%20Ana%20&page=3&pageSize=5000",
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        await _factory.Repo.Received(1).ListAsync(
            Arg.Is<ManualIdentityReviewFilter>(f => f.Status == "pendiente_revision_manual" && f.Origin == "mandatario" && f.Text == "Ana"),
            Arg.Is(200), // página 3 con pageSize acotado a 100
            Arg.Is(100),
            Arg.Any<CancellationToken>());
        using var body = await JsonAsync(response);
        body.RootElement.GetProperty("page").GetInt32().Should().Be(3);
        body.RootElement.GetProperty("pageSize").GetInt32().Should().Be(100);
    }

    [Theory]
    [InlineData("status=kyverum_en_proceso")]
    [InlineData("origin=otro")]
    public async Task Estado_u_origen_invalidos_responden_400_sin_consultar(string query)
    {
        var response = await ClientFor("SuperAdmin", null).GetAsync($"{Url}?{query}", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        _factory.Repo.ReceivedCalls().Should().BeEmpty();
    }

    private static async Task<JsonDocument> JsonAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

    private HttpClient ClientFor(string role, Guid? tenant, IReadOnlyList<string>? permissions = null)
    {
        var claims = new List<Claim>
        {
            new("sub", UserId.ToString()),
            new("role", role),
            new("role_code", role),
        };
        if (tenant is { } t)
            claims.Add(new Claim("tenant_id", t.ToString()));
        claims.AddRange((permissions ?? []).Select(p => new Claim("permissions", p)));

        var token = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = "https://api.flit.co",
            Audience = "flit-api",
            Subject = new ClaimsIdentity(claims),
            Expires = DateTime.UtcNow.AddHours(1),
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(new string('k', 64))), SecurityAlgorithms.HmacSha256),
        });

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    public sealed class ManualFactory : WebApplicationFactory<Program>
    {
        public IManualIdentityReviewReadRepository Repo { get; } = Substitute.For<IManualIdentityReviewReadRepository>();

        public ManualFactory()
        {
            var now = DateTimeOffset.UtcNow;
            IReadOnlyList<ManualIdentityReviewRow> rows =
            [
                new(Guid.NewGuid(), "Ana Gómez", "1001", "Compañía A", "tramite", "pendiente_revision_manual", now.AddMinutes(-90), now.AddMinutes(-60)),
                new(Guid.NewGuid(), "Beto Ruiz", "2002", "Compañía B", "prevalidacion", "rechazado", now.AddMinutes(-600), null),
            ];
            Repo.ListAsync(Arg.Any<ManualIdentityReviewFilter>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
                .Returns(_ => (rows, rows.Count));
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            ArgumentNullException.ThrowIfNull(builder);
            builder.ConfigureTestServices(services =>
            {
                services.AddScoped(_ => Repo);
                services.AddScoped<ITenantScopeResolver>(_ => new SingleScopeResolver());
                services.AddScoped<ITransitOfficeTenantProbe>(_ => new NoTransitOffices());
            });
        }
    }

    private sealed class SingleScopeResolver : ITenantScopeResolver
    {
        public Task<TenantScope> ResolveAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
            Task.FromResult(TenantScope.Single(tenantId));
    }

    private sealed class NoTransitOffices : ITransitOfficeTenantProbe
    {
        public Task<bool> IsTransitOfficeAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);
    }
}
