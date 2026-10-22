using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Flit.Analytics.Application.Abstractions;
using Flit.Analytics.Application.Dtos;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace Flit.Admin.Tests.Authorization;

/// <summary>
/// HU #13664 (Feature #13636 F-F, Épica #12753, criterio FF-4) — «los listados combinados filtran los registros de los tipos
/// sin Ver»: con <c>Security:ModulePermissionEnforcement:Enabled=true</c>, el overview del dashboard/Reportes y el detalle de
/// trámites de Reportes NO cuentan ni listan la familia sin Ver (aquí Traspasos), porque el endpoint le pasa al repositorio
/// las familias legibles del usuario. Con las tres familias, o con el flag apagado, no cambia nada (<c>null</c> = sin filtro).
/// <para>El repositorio va sustituido por uno en memoria que aplica la lista recibida como lo hace el SQL (la categoría sale
/// de la familia del tipo): aquí se prueba la cadena endpoint → handler → repositorio; el predicado SQL lo cubre
/// <c>Flit.Integration.Tests/Tramites/FamilyAnalyticsSqlTests</c> contra PostgreSQL real.</para>
/// <para>Uso de ejemplo: <c>GET /api/v1/analytics/overview</c> con <c>matriculas.read</c> + <c>otros-tramites.read</c> (sin
/// <c>traspasos.read</c>) → sin categoría <c>traspasos</c>.</para>
/// </summary>
public sealed class FamilyAnalyticsEnforcementTests : IClassFixture<FamilyAnalyticsEnforcementTests.Hosts>
{
    private static readonly Guid Tenant = Guid.Parse("0c000000-0000-4000-8000-0000000136b1");
    private const string Overview = "/api/v1/analytics/overview";
    private const string Detalle = "/api/v1/analytics/procedures?from=2026-01-01&to=2026-12-31";

    // Permisos de módulo que exigen las rutas (overview: resumen/dashboard; detalle: cualquier pestaña de Reportes).
    private static readonly string[] Modulo = ["reportes.read", "reportes.resumen.read", "dashboard.read"];

    private readonly Hosts _hosts;

    public FamilyAnalyticsEnforcementTests(Hosts hosts) => _hosts = hosts;

    /// <summary>Hosts (flag apagado / encendido) con el repositorio analítico en memoria.</summary>
    public sealed class Hosts : IDisposable
    {
        private readonly WebApplicationFactory<Program> _root = new();

        public FakeAnalyticsRepository Repo { get; } = new();

        public WebApplicationFactory<Program> Apagado { get; }

        public WebApplicationFactory<Program> Encendido { get; }

        public Hosts()
        {
            Apagado = _root.WithWebHostBuilder(b => b.ConfigureTestServices(s => s.AddScoped<IAnalyticsReadRepository>(_ => Repo)));
            Encendido = _root.WithWebHostBuilder(b =>
            {
                b.UseSetting("Security:ModulePermissionEnforcement:Enabled", "true");
                b.ConfigureTestServices(s => s.AddScoped<IAnalyticsReadRepository>(_ => Repo));
            });
        }

        public void Dispose()
        {
            Encendido.Dispose();
            Apagado.Dispose();
            _root.Dispose();
        }
    }

    [Fact]
    public async Task Overview_SinVerTraspasos_NoCuentaTraspasos()
    {
        var categorias = await Categorias(_hosts.Encendido, Token([.. Modulo, "matriculas.read", "otros-tramites.read"]));

        categorias.Should().BeEquivalentTo(["matriculas", "otros"], "la familia sin Ver no aparece en el conteo del inicio ni de Reportes");
        _hosts.Repo.UltimasFamilias.Should().BeEquivalentTo(["MATRICULAS", "OTROS"]);
    }

    [Fact]
    public async Task DetalleDeReportes_SinVerTraspasos_NoListaNiCuentaTraspasos()
    {
        var (categorias, total) = await Detalle_(_hosts.Encendido, Token([.. Modulo, "matriculas.read", "otros-tramites.read"]));

        categorias.Should().NotContain("traspasos").And.HaveCount(2);
        total.Should().Be(2, "el total del universo tampoco cuenta la familia sin Ver");
        _hosts.Repo.UltimasFamilias.Should().BeEquivalentTo(["MATRICULAS", "OTROS"]);
    }

    [Fact]
    public async Task SinNingunVer_ElOverviewQuedaVacio()
    {
        var categorias = await Categorias(_hosts.Encendido, Token(Modulo));

        categorias.Should().BeEmpty("vacío = ninguna familia visible, nunca «sin filtro»");
        _hosts.Repo.UltimasFamilias.Should().NotBeNull().And.BeEmpty();
    }

    [Fact]
    public async Task ConLasTresFamilias_NoCambiaNada()
    {
        var token = Token([.. Modulo, "matriculas.read", "traspasos.read", "otros-tramites.read"]);

        (await Categorias(_hosts.Encendido, token)).Should().BeEquivalentTo(["matriculas", "traspasos", "otros"]);
        _hosts.Repo.UltimasFamilias.Should().BeNull("las tres legibles = sin restricción");

        var (categorias, total) = await Detalle_(_hosts.Encendido, token);
        categorias.Should().Contain("traspasos");
        total.Should().Be(3);
        _hosts.Repo.UltimasFamilias.Should().BeNull();
    }

    [Fact]
    public async Task ConElFlagApagado_NoCambiaNada()
    {
        var token = Token("dashboard.read");

        (await Categorias(_hosts.Apagado, token)).Should().BeEquivalentTo(["matriculas", "traspasos", "otros"]);
        _hosts.Repo.UltimasFamilias.Should().BeNull("con el flag apagado los permisos por familia no se exigen");

        var (_, total) = await Detalle_(_hosts.Apagado, Token("reportes.read"));
        total.Should().Be(3);
        _hosts.Repo.UltimasFamilias.Should().BeNull();
    }

    // ── helpers ──────────────────────────────────────────────────────────────────────────────

    private static async Task<List<string>> Categorias(WebApplicationFactory<Program> host, string token)
    {
        using var doc = await Get(host, Overview, token);
        return [.. doc.RootElement.GetProperty("categories").EnumerateArray().Select(c => c.GetProperty("category").GetString()!)];
    }

    private static async Task<(List<string> Categorias, int Total)> Detalle_(WebApplicationFactory<Program> host, string token)
    {
        using var doc = await Get(host, Detalle, token);
        var categorias = doc.RootElement.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("category").GetString()!).ToList();
        return (categorias, doc.RootElement.GetProperty("totalCount").GetInt32());
    }

    private static async Task<JsonDocument> Get(WebApplicationFactory<Program> host, string url, string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await host.CreateClient().SendAsync(request, TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        return JsonDocument.Parse(body);
    }

    private static string Token(params string[] permisos)
    {
        var claims = new List<Claim>
        {
            new("sub", "13664000-0000-4000-8000-000000000002"),
            new("role", "Radicador"),
            new("role_code", "Radicador"),
            new("tenant_id", Tenant.ToString()),
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

    /// <summary>
    /// Un trámite por familia; aplica la lista de familias recibida igual que el predicado SQL (<c>null</c> = todas,
    /// vacío = ninguna) y recuerda la última lista para afirmar qué le pasó el endpoint.
    /// </summary>
    public sealed class FakeAnalyticsRepository : IAnalyticsReadRepository
    {
        private static readonly (string Familia, ProcedureDetailDto Fila)[] Filas =
        [
            ("MATRICULAS", Fila("matriculas")),
            ("TRASPASO", Fila("traspasos")),
            ("OTROS", Fila("otros")),
        ];

        public IReadOnlyList<string>? UltimasFamilias { get; private set; }

        public Task<IReadOnlyList<CategoryMetricsDto>> GetOverviewAsync(
            Guid? tenantId, DateOnly? fromDate, DateOnly? toDate, IReadOnlyList<string>? allowedFamilies = null, CancellationToken ct = default)
        {
            IReadOnlyList<CategoryMetricsDto> items = [.. Visibles(allowedFamilies).Select(f => new CategoryMetricsDto(f.Category, 1, [new StatusCountDto(f.Status, 1)]))];
            return Task.FromResult(items);
        }

        public Task<ProcedureDetailsPageDto> GetProcedureDetailsAsync(
            Guid tenantId, DateOnly fromDate, DateOnly toDate, string? category, string? status, int page, int pageSize,
            IReadOnlyList<string>? allowedFamilies = null, CancellationToken ct = default)
        {
            var items = Visibles(allowedFamilies).ToList();
            return Task.FromResult(new ProcedureDetailsPageDto(items, items.Count, page, pageSize));
        }

        public Task<IReadOnlyList<TopProducerDto>> GetTopProducersAsync(
            Guid? tenantId, DateOnly fromDate, DateOnly toDate, int limit, IReadOnlyList<string>? allowedFamilies = null, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task ExportProcedureDetailsAsync(
            Guid tenantId, DateOnly fromDate, DateOnly toDate, string? category, string? status,
            Func<ProcedureDetailDto, CancellationToken, Task> onRowAsync, IReadOnlyList<string>? allowedFamilies = null, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<MonthlyTrendPointDto>> GetMonthlyTrendAsync(
            Guid? tenantId, DateOnly fromDate, DateOnly toDate, IReadOnlyList<string>? allowedFamilies = null, CancellationToken ct = default) =>
            throw new NotSupportedException();

        private IEnumerable<ProcedureDetailDto> Visibles(IReadOnlyList<string>? allowedFamilies)
        {
            UltimasFamilias = allowedFamilies;
            return Filas.Where(f => allowedFamilies is null || allowedFamilies.Contains(f.Familia)).Select(f => f.Fila);
        }

        private static ProcedureDetailDto Fila(string categoria) =>
            new(Guid.NewGuid(), $"REF-{categoria}", categoria, categoria, "entregado", "Ana", null, null);
    }
}
