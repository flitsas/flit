using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using Flit.Api.Authorization;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace Flit.Admin.Tests.Authorization;

/// <summary>
/// HU #13659 (Feature #13105 F-E, Épica #12753) — exigencia de los slugs de Reportes por ruta, detrás de
/// <c>Security:ModulePermissionEnforcement:Enabled</c> (apagado por defecto). Cada ruta declara los slugs que la abren; las
/// rutas pueden responder 400/404 por datos: lo que importa es que no pasen a 401/403.
/// <para>Uso de ejemplo: <c>Llamar(_encendido, "GET /api/v1/analytics/usage", Token("AdminCompany", ["reportes.uso.read"]))</c>.</para>
/// </summary>
public sealed class ReportesPermissionEnforcementTests : IClassFixture<ReportesPermissionEnforcementTests.Hosts>
{
    private static readonly string[] Todas = ReportesPermissions.CualquierPestana;
    private static readonly string[] Resumen = [ReportesPermissions.Resumen, ReportesPermissions.Read, "dashboard.read"];
    private static readonly string[] Programacion = [ReportesPermissions.Programacion];
    private static readonly string[] Consultas = [ReportesPermissions.Consultas];

    /// <summary>Ruta → slugs que la abren (uno basta).</summary>
    private static readonly (string Firma, string[] Slugs)[] Mapa =
    [
        ("GET /api/v1/analytics/overview", Resumen),
        ("GET /api/v1/analytics/monthly-trend", Resumen),
        ("GET /api/v1/analytics/productivity/top", [ReportesPermissions.Productividad]),
        ("GET /api/v1/analytics/procedures", Todas),
        ("GET /api/v1/analytics/export/excel", Todas),
        ("POST /api/v1/analytics/export/executive-pdf", Todas),
        ("GET /api/v1/analytics/ot-metrics", [ReportesPermissions.Ot, ReportesPermissions.Operacion]),
        ("GET /api/v1/analytics/funnel", [ReportesPermissions.Operacion]),
        ("GET /api/v1/analytics/usage", [ReportesPermissions.Uso]),
        ("GET /api/v1/analytics/live-overview", Todas),
        ("GET /api/v1/analytics/queries/fields", Consultas),
        ("POST /api/v1/analytics/queries/run", Consultas),
        ("GET /api/v1/analytics/queries/saved", Consultas),
        ("GET /api/v1/analytics/report-schedules", Programacion),
        ("POST /api/v1/analytics/report-schedules", Programacion),
        ("GET /api/v1/analytics/alert-rules", Programacion),
        ("GET /api/v1/analytics/alert-events", Programacion),
    ];

    private readonly WebApplicationFactory<Program> _apagado;
    private readonly WebApplicationFactory<Program> _encendido;

    public ReportesPermissionEnforcementTests(Hosts hosts)
    {
        _apagado = hosts.Apagado;
        _encendido = hosts.Encendido;
    }

    /// <summary>Un host con el flag por defecto y otro con el flag encendido, compartidos por toda la clase.</summary>
    public sealed class Hosts : IDisposable
    {
        public WebApplicationFactory<Program> Apagado { get; } = new();

        public WebApplicationFactory<Program> Encendido { get; }

        public Hosts() =>
            Encendido = Apagado.WithWebHostBuilder(b => b.UseSetting("Security:ModulePermissionEnforcement:Enabled", "true"));

        public void Dispose()
        {
            Encendido.Dispose();
            Apagado.Dispose();
        }
    }

    public static TheoryData<string> Rutas()
    {
        var data = new TheoryData<string>();
        foreach (var (firma, _) in Mapa) data.Add(firma);
        return data;
    }

    public static TheoryData<string, string> RutasYSlug()
    {
        var data = new TheoryData<string, string>();
        foreach (var (firma, slugs) in Mapa)
            foreach (var slug in slugs) data.Add(firma, slug);
        return data;
    }

    [Fact]
    public void AC3_LosOchoSlugsDeLaHuSeExigenEnAlgunaRuta()
    {
        string[] ocho = [.. Todas, ReportesPermissions.Programacion];
        ocho.Should().HaveCount(8);
        Mapa.SelectMany(m => m.Slugs).Distinct().Intersect(ocho).Should().BeEquivalentTo(ocho);
    }

    [Theory]
    [MemberData(nameof(Rutas))]
    public async Task FlagApagado_SinSlugs_NoRecibe403PorPermiso(string firma)
    {
        var response = await Llamar(_apagado, firma, Token("AdminCompany", []));

        response.StatusCode.Should().NotBe(HttpStatusCode.Forbidden, firma);
        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized, firma);
    }

    [Theory]
    [MemberData(nameof(RutasYSlug))]
    public async Task AC1_FlagEncendido_ConElSlug_ObtieneElMismoEstadoQueConElFlagApagado(string firma, string slug)
    {
        var antes = await Llamar(_apagado, firma, Token("AdminCompany", [slug]));
        var despues = await Llamar(_encendido, firma, Token("AdminCompany", [slug]));

        despues.StatusCode.Should().NotBe(HttpStatusCode.Forbidden, $"{firma} con {slug}");
        despues.StatusCode.Should().Be(antes.StatusCode, firma);
    }

    [Theory]
    [MemberData(nameof(Rutas))]
    public async Task AC2_FlagEncendido_SinElSlug_403(string firma)
    {
        var response = await Llamar(_encendido, firma, Token("AdminCompany", ["tramites.read", "reportes.detallados.read"]));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden, firma);
    }

    [Fact]
    public async Task SuperAdmin_ConservaSuBypass_ConElFlagEncendido()
    {
        foreach (var firma in new[] { "GET /api/v1/analytics/usage", "GET /api/v1/analytics/overview", "GET /api/v1/analytics/queries/fields", "GET /api/v1/analytics/alert-rules" })
        {
            var response = await Llamar(_encendido, firma, Token("SuperAdmin", []));
            response.StatusCode.Should().NotBe(HttpStatusCode.Forbidden, firma);
            response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized, firma);
        }
    }

    [Fact]
    public async Task SinToken_401_ConElFlagEncendido()
    {
        using var response = await _encendido.CreateClient().GetAsync("/api/v1/analytics/usage", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private static async Task<HttpResponseMessage> Llamar(WebApplicationFactory<Program> factory, string firma, string token)
    {
        var partes = firma.Split(' ', 2);
        using var request = new HttpRequestMessage(new HttpMethod(partes[0]), partes[1]);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (partes[0] is "POST" or "PUT" or "PATCH")
            request.Content = new StringContent("{}", Encoding.UTF8, "application/json");

        return await factory.CreateClient().SendAsync(request, TestContext.Current.CancellationToken);
    }

    private static string Token(string role, IEnumerable<string> permisos)
    {
        var claims = new List<Claim>
        {
            new("sub", "13659000-0000-4000-8000-000000000001"),
            new("role", role),
            new("role_code", role),
            new("tenant_id", "0e000000-0000-4000-8000-000000013659"),
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
}
