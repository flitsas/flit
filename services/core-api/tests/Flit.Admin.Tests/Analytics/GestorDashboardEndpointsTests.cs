using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using Flit.Analytics.Application.Abstractions;
using Flit.Analytics.Application.Dtos;
using Flit.Analytics.Application.Queries.GestorDashboard;
using Flit.Tramites.Domain.Tramites.Estados;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace Flit.Admin.Tests.Analytics;

/// <summary>
/// Inicio del Gestor (rediseño de los 3 dashboards) — capa HTTP de <c>/api/v1/analytics/gestor/*</c>: autorización,
/// aislamiento de tenant y Ver por familia. El repositorio va en memoria y recuerda qué tenant y qué familias recibió;
/// el SQL no se prueba aquí.
/// <para>Uso de ejemplo: <c>GET /api/v1/analytics/gestor/cola-inactividad?estado=preparado</c> con el JWT de una compañía
/// → consulta SOLO esa compañía; con <c>tenantId</c> de otra → 403 sin consultar.</para>
/// </summary>
public sealed class GestorDashboardEndpointsTests : IClassFixture<GestorDashboardEndpointsTests.Hosts>
{
    private static readonly Guid Tenant = Guid.Parse("14a00000-0000-4000-8000-0000000000a1");
    private static readonly Guid OtroTenant = Guid.Parse("14a00000-0000-4000-8000-0000000000b2");
    private const string Cola = "/api/v1/analytics/gestor/cola-inactividad";
    private static readonly string[] Modulo = ["dashboard.read"];

    private readonly Hosts _hosts;

    public GestorDashboardEndpointsTests(Hosts hosts)
    {
        _hosts = hosts;
        _hosts.Repo.Reset();
    }

    public sealed class Hosts : IDisposable
    {
        private readonly WebApplicationFactory<Program> _root = new();

        public FakeRepo Repo { get; } = new();

        public WebApplicationFactory<Program> Apagado { get; }

        public WebApplicationFactory<Program> Encendido { get; }

        public Hosts()
        {
            Apagado = _root.WithWebHostBuilder(b => b.ConfigureTestServices(s => s.AddScoped<IGestorDashboardReadRepository>(_ => Repo)));
            Encendido = _root.WithWebHostBuilder(b =>
            {
                b.UseSetting("Security:ModulePermissionEnforcement:Enabled", "true");
                b.ConfigureTestServices(s => s.AddScoped<IGestorDashboardReadRepository>(_ => Repo));
            });
        }

        public void Dispose()
        {
            Encendido.Dispose();
            Apagado.Dispose();
            _root.Dispose();
        }
    }

    // ── Aislamiento de tenant ──────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(Cola)]
    public async Task ElTenantSaleDelToken(string url)
    {
        var (status, body) = await Get(_hosts.Apagado, url, Token(Tenant, Modulo));

        status.Should().Be(HttpStatusCode.OK, body);
        _hosts.Repo.UltimoTenant.Should().Be(Tenant);
    }

    [Theory]
    [InlineData(Cola)]
    public async Task OtroTenantPorQuery_403SinConsultar(string url)
    {
        var (status, _) = await Get(_hosts.Apagado, $"{url}?tenantId={OtroTenant}", Token(Tenant, Modulo));

        status.Should().Be(HttpStatusCode.Forbidden);
        _hosts.Repo.Llamadas.Should().Be(0);
    }

    [Fact]
    public async Task SuperAdminSinCompania_400SinConsultar()
    {
        var (status, _) = await Get(_hosts.Apagado, Cola, Token(Tenant, Modulo, rol: "SuperAdmin"));

        status.Should().Be(HttpStatusCode.BadRequest, "no hay vista global: son cifras de una compañía");
        _hosts.Repo.Llamadas.Should().Be(0);
    }

    [Fact]
    public async Task SuperAdminConCompania_ConsultaEsaCompania()
    {
        var (status, _) = await Get(_hosts.Apagado, $"{Cola}?tenantId={OtroTenant}", Token(Tenant, Modulo, rol: "SuperAdmin"));

        status.Should().Be(HttpStatusCode.OK);
        _hosts.Repo.UltimoTenant.Should().Be(OtroTenant);
    }

    // ── Autorización ───────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(Cola)]
    public async Task SinToken_401(string url)
    {
        using var response = await _hosts.Apagado.CreateClient().GetAsync(url, TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData(Cola)]
    public async Task ConEnforcement_SinPermisoDelInicio_403(string url)
    {
        var (status, _) = await Get(_hosts.Encendido, url, Token(Tenant, ["matriculas.read"]));

        status.Should().Be(HttpStatusCode.Forbidden);
        _hosts.Repo.Llamadas.Should().Be(0);
    }

    [Fact] // Dato por fila: con el permiso del Inicio pero sin ver ninguna familia de trámites → 403 (gate del listado).
    public async Task ConEnforcement_ConDashboardSinLecturaDeTramites_403()
    {
        var (status, _) = await Get(_hosts.Encendido, Cola, Token(Tenant, Modulo));

        status.Should().Be(HttpStatusCode.Forbidden);
        _hosts.Repo.Llamadas.Should().Be(0);
    }

    // ── Ver por familia (HU #13664) ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Cola_SinVerTraspasos_NoLosLista()
    {
        var (status, body) = await Get(_hosts.Encendido, Cola, Token(Tenant, [.. Modulo, "matriculas.read", "otros-tramites.read"]));

        status.Should().Be(HttpStatusCode.OK, body);
        _hosts.Repo.UltimasFamilias.Should().BeEquivalentTo(["MATRICULAS", "OTROS"]);
    }

    [Fact]
    public async Task Cola_PedirFamiliaSinVer_ColaVaciaSinConsultar()
    {
        var (status, body) = await Get(_hosts.Encendido, $"{Cola}?familia=traspasos", Token(Tenant, [.. Modulo, "matriculas.read"]));

        status.Should().Be(HttpStatusCode.OK);
        body.Should().Contain("\"total\":0");
        _hosts.Repo.Llamadas.Should().Be(0);
    }

    // ── Validación de entrada ──────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(Cola + "?estado=inventado")]
    [InlineData(Cola + "?familia=vehicular")]
    public async Task EntradaInvalida_400(string url)
    {
        var (status, _) = await Get(_hosts.Apagado, url, Token(Tenant, Modulo));
        status.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact] // El vocabulario del handler es una copia de TramiteEstado (Analytics no lo referencia): que no se aparte.
    public void Vocabulario_CoincideConTramiteEstado()
    {
        GestorDashboardVocabulario.Estados.Should().BeEquivalentTo([.. TramiteEstado.Todos, TramiteEstado.Subsanacion]);
        GestorDashboardVocabulario.EstadosSinFinales.Should().NotIntersectWith(TramiteEstado.Finales);
    }

    // ── helpers ──────────────────────────────────────────────────────────────────────────────────

    private static async Task<(HttpStatusCode Status, string Body)> Get(WebApplicationFactory<Program> host, string url, string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await host.CreateClient().SendAsync(request, TestContext.Current.CancellationToken);
        return (response.StatusCode, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    private static string Token(Guid tenant, string[] permisos, string rol = "Radicador")
    {
        var claims = new List<Claim>
        {
            new("sub", "14a00000-0000-4000-8000-000000000002"),
            new("role", rol),
            new("role_code", rol),
            new("tenant_id", tenant.ToString()),
            new("entity_type", "COMPANY"),
        };
        claims.AddRange(permisos.Select(p => new Claim("permissions", p)));

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

    /// <summary>Repositorio en memoria: cuenta las llamadas y recuerda el último tenant y las últimas familias.</summary>
    public sealed class FakeRepo : IGestorDashboardReadRepository
    {
        public int Llamadas { get; private set; }

        public Guid? UltimoTenant { get; private set; }

        public IReadOnlyList<string>? UltimasFamilias { get; private set; }

        public void Reset()
        {
            Llamadas = 0;
            UltimoTenant = null;
            UltimasFamilias = null;
        }

        public Task<GestorDashboardColaDto> GetColaInactividadAsync(
            Guid tenantId, IReadOnlyList<string> estados, IReadOnlyList<string>? families, int limit, CancellationToken ct = default)
        {
            Registrar(tenantId, families);
            return Task.FromResult(new GestorDashboardColaDto([], 0));
        }

        private void Registrar(Guid tenantId, IReadOnlyList<string>? families)
        {
            Llamadas++;
            UltimoTenant = tenantId;
            UltimasFamilias = families;
        }
    }
}
