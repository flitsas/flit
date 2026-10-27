using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace Flit.Admin.Tests.Authorization;

/// <summary>
/// HU #13660 (Feature #13105 F-E, Épica #12753) — Reportes detallados exige reportes.detallados.read / .export en la API,
/// detrás de <c>Security:ModulePermissionEnforcement:Enabled</c> (apagado por defecto). Las rutas pueden responder 400 por
/// datos (faltan from/to): lo que importa es que no pasen a 401/403.
/// <para>Uso de ejemplo: <c>Llamar(_encendido, "/api/v1/detailed-report/procedures", Token("AdminCompany", ["reportes.detallados.read"]))</c></para>
/// </summary>
public sealed class DetailedReportPermissionEnforcementTests : IClassFixture<DetailedReportPermissionEnforcementTests.Hosts>
{
    private static readonly Guid Tenant = Guid.Parse("0e000000-0000-4000-8000-000000013660");

    private readonly WebApplicationFactory<Program> _apagado;
    private readonly WebApplicationFactory<Program> _encendido;

    public DetailedReportPermissionEnforcementTests(Hosts hosts)
    {
        _apagado = hosts.Apagado;
        _encendido = hosts.Encendido;
    }

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

    /// <summary>Ruta y slug que la protege.</summary>
    public static TheoryData<string, string> Rutas() => new()
    {
        { "/api/v1/detailed-report/procedures", "reportes.detallados.read" },
        { "/api/v1/detailed-report/procedures/export", "reportes.detallados.export" },
    };

    [Theory]
    [MemberData(nameof(Rutas))]
    public async Task AC1_ConElSlug_MismoEstadoQueConElFlagApagado(string ruta, string slug)
    {
        var antes = await Llamar(_apagado, ruta, Token("AdminCompany", []));
        var despues = await Llamar(_encendido, ruta, Token("AdminCompany", [slug]));

        despues.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized).And.NotBe(HttpStatusCode.Forbidden);
        despues.StatusCode.Should().Be(antes.StatusCode, ruta);
    }

    [Theory]
    [MemberData(nameof(Rutas))]
    public async Task AC2_SinElSlug_403_YElSlugDeLaOtraRutaNoAbre(string ruta, string slug)
    {
        (await Llamar(_encendido, ruta, Token("AdminCompany", []))).StatusCode.Should().Be(HttpStatusCode.Forbidden, ruta);

        var otro = slug.EndsWith(".read", StringComparison.Ordinal) ? "reportes.detallados.export" : "reportes.detallados.read";
        (await Llamar(_encendido, ruta, Token("AdminCompany", [otro]))).StatusCode.Should().Be(HttpStatusCode.Forbidden, ruta);
    }

    [Theory]
    [MemberData(nameof(Rutas))]
    public async Task FlagApagado_SinElSlug_NoRecibe403(string ruta, string slug)
    {
        _ = slug;
        var response = await Llamar(_apagado, ruta, Token("AdminCompany", []));

        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized).And.NotBe(HttpStatusCode.Forbidden);
    }

    [Theory]
    [MemberData(nameof(Rutas))]
    public async Task SuperAdmin_ConservaSuBypass_ConElFlagEncendido(string ruta, string slug)
    {
        _ = slug;
        var response = await Llamar(_encendido, ruta, Token("SuperAdmin", []));

        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized).And.NotBe(HttpStatusCode.Forbidden);
    }

    private static async Task<HttpResponseMessage> Llamar(WebApplicationFactory<Program> factory, string ruta, string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, ruta);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await factory.CreateClient().SendAsync(request, TestContext.Current.CancellationToken);
    }

    private static string Token(string role, IEnumerable<string> permisos)
    {
        var claims = new List<Claim>
        {
            new("sub", "13660000-0000-4000-8000-000000000001"),
            new("role", role),
            new("role_code", role),
            new("tenant_id", Tenant.ToString()),
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
