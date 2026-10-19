using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using Flit.Api.Authorization;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace Flit.Admin.Tests.Authorization;

/// <summary>
/// HU #13661 (Feature #13105 F-E, Épica #12753) — Improntas exige <c>improntas.read</c> / <c>improntas.generate</c> detrás de
/// <c>Security:ModulePermissionEnforcement:Enabled</c>. Las rutas están bajo SuperAdminPolicy (solo SuperAdmin, que bypassa el
/// slug), así que el 403 por permiso es inobservable por HTTP: se verifica el requisito en el endpoint y el 403 por rol.
/// <para>Uso de ejemplo: <c>[Theory] [MemberData(nameof(Rutas))] Llamar(_encendido, "GET", ruta, Token("SuperAdmin"))</c></para>
/// </summary>
public sealed class ImprontasPermissionEnforcementTests : IClassFixture<ImprontasPermissionEnforcementTests.Hosts>
{
    private readonly WebApplicationFactory<Program> _apagado;
    private readonly WebApplicationFactory<Program> _encendido;

    public ImprontasPermissionEnforcementTests(Hosts hosts)
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

    public static TheoryData<string, string, string> Rutas() => new()
    {
        { "GET", "/api/v1/admin/improntas", "improntas.read" },
        { "POST", "/api/v1/admin/improntas/generate", "improntas.generate" },
        { "POST", "/api/v1/admin/improntas/firmas/backfill", "improntas.generate" },
    };

    // El backfill toca la BD y tarda minutos sin ella: su estado no es determinista, se cubre en AC2/AC3.
    public static TheoryData<string, string, string> RutasSinBackfill() => new()
    {
        { "GET", "/api/v1/admin/improntas", "improntas.read" },
        { "POST", "/api/v1/admin/improntas/generate", "improntas.generate" },
    };

    // AC3: cada ruta de Improntas del lado no-OT declara su slug.
    [Theory]
    [MemberData(nameof(Rutas))]
    public void AC3_LaRutaDeclaraElRequisitoDelSlug(string metodo, string ruta, string slug)
    {
        var endpoint = _apagado.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Single(e => e.RoutePattern.RawText?.TrimEnd('/') == ruta && (e.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods.Contains(metodo) ?? false));

        endpoint.Metadata.GetOrderedMetadata<AuthorizationPolicy>()
            .SelectMany(p => p.Requirements).OfType<ModulePermissionRequirement>()
            .Select(r => r.Slug).Should().Contain(slug);
    }

    // AC1: SuperAdmin (sin el slug en el claim) obtiene el mismo estado con el flag encendido que apagado.
    [Theory]
    [MemberData(nameof(RutasSinBackfill))]
    public async Task AC1_SuperAdmin_MismoEstadoConFlagEncendidoYApagado(string metodo, string ruta, string slug)
    {
        _ = slug;
        var antes = await Llamar(_apagado, metodo, ruta, Token("SuperAdmin", []));
        var despues = await Llamar(_encendido, metodo, ruta, Token("SuperAdmin", []));

        despues.StatusCode.Should().NotBe(HttpStatusCode.Forbidden).And.NotBe(HttpStatusCode.Unauthorized);
        despues.StatusCode.Should().Be(antes.StatusCode);
    }

    // Flag apagado: sin el slug y sin ser SuperAdmin, la política de rol sigue mandando (403), no un cambio de comportamiento.
    // AC2: con el flag encendido, quien no es SuperAdmin ni trae el slug → 403.
    [Theory]
    [MemberData(nameof(Rutas))]
    public async Task AC2_SinSlugNiSuperAdmin_403_ConFlagApagadoYEncendido(string metodo, string ruta, string slug)
    {
        _ = slug;
        (await Llamar(_apagado, metodo, ruta, Token("admin_tramites", []))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await Llamar(_encendido, metodo, ruta, Token("admin_tramites", []))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private static async Task<HttpResponseMessage> Llamar(WebApplicationFactory<Program> factory, string metodo, string ruta, string token)
    {
        using var request = new HttpRequestMessage(new HttpMethod(metodo), ruta);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (metodo == "POST")
        {
            request.Content = new StringContent("{}", Encoding.UTF8, "application/json");
        }

        return await factory.CreateClient().SendAsync(request, TestContext.Current.CancellationToken);
    }

    private static string Token(string role, IEnumerable<string> permisos)
    {
        var claims = new List<Claim>
        {
            new("sub", "13661000-0000-4000-8000-000000000001"),
            new("role", role),
            new("role_code", role),
            new("tenant_id", "0e000000-0000-4000-8000-000000013661"),
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
