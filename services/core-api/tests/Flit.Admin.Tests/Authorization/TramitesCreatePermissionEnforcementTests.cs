using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using Flit.Api.Authorization;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace Flit.Admin.Tests.Authorization;

/// <summary>
/// HU #13662 (Feature #13105 F-E, Épica #12753) — las rutas que crean trámites exigen <c>tramites.create</c> detrás de
/// <c>Security:ModulePermissionEnforcement:Enabled</c> (apagado por defecto). Las rutas pueden responder 400/404/422 por datos:
/// lo que importa es que no pasen a 401/403 por permiso.
/// <para>Uso de ejemplo: un Theory con MemberData(Rutas) llama cada ruta con el flag encendido y un token con/sin el slug.</para>
/// </summary>
public sealed class TramitesCreatePermissionEnforcementTests : IClassFixture<TramitesCreatePermissionEnforcementTests.Hosts>
{
    private const string Slug = "tramites.create";
    private static readonly Guid Tenant = Guid.Parse("0c000000-0000-4000-8000-000000013662");

    private readonly WebApplicationFactory<Program> _apagado;
    private readonly WebApplicationFactory<Program> _encendido;

    public TramitesCreatePermissionEnforcementTests(Hosts hosts)
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

    /// <summary>Las 4 rutas que crean trámites (carga masiva incluida: crea los trámites del lote).</summary>
    public static TheoryData<string> Rutas() => new()
    {
        "POST /api/v1/tramites",
        "POST /api/v1/tramites/instances",
        "POST /api/v1/tramites/instances/from-consulta",
        "POST /api/v1/tramites/carga-masiva/lotes",
    };

    [Fact]
    public void FlagApagadoPorDefecto() =>
        _apagado.Services.GetRequiredService<IOptions<ModulePermissionEnforcementOptions>>().Value.Enabled.Should().BeFalse();

    [Theory]
    [MemberData(nameof(Rutas))]
    public async Task FlagApagado_SinElSlug_NoRecibe403(string firma)
    {
        var response = await Llamar(_apagado, firma, Token("Radicador", []));

        response.StatusCode.Should().NotBe(HttpStatusCode.Forbidden, firma);
        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized, firma);
    }

    // AC1: con el slug, mismo estado que antes (flag apagado).
    [Theory]
    [MemberData(nameof(Rutas))]
    public async Task AC1_FlagEncendido_ConElSlug_MismoEstadoQueConElFlagApagado(string firma)
    {
        var antes = await Llamar(_apagado, firma, Token("Radicador", [Slug]));
        var despues = await Llamar(_encendido, firma, Token("Radicador", [Slug]));

        despues.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized, firma);
        despues.StatusCode.Should().Be(antes.StatusCode, firma);
    }

    // AC2: sin el slug (con otros permisos) → 403.
    [Theory]
    [MemberData(nameof(Rutas))]
    public async Task AC2_FlagEncendido_SinElSlug_403(string firma)
    {
        var response = await Llamar(_encendido, firma, Token("Radicador", ["tramites.read", "dashboard.read"]));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden, firma);
    }

    [Theory]
    [MemberData(nameof(Rutas))]
    public async Task FlagEncendido_SuperAdmin_ConservaSuBypass(string firma)
    {
        var response = await Llamar(_encendido, firma, Token("SuperAdmin", []));

        response.StatusCode.Should().NotBe(HttpStatusCode.Forbidden, firma);
        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized, firma);
    }

    private static async Task<HttpResponseMessage> Llamar(WebApplicationFactory<Program> factory, string firma, string token)
    {
        var partes = firma.Split(' ', 2);
        using var request = new HttpRequestMessage(new HttpMethod(partes[0]), partes[1]);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = partes[1].EndsWith("/lotes", StringComparison.Ordinal)
            ? new MultipartFormDataContent()
            : new StringContent("{}", Encoding.UTF8, "application/json");

        return await factory.CreateClient().SendAsync(request, TestContext.Current.CancellationToken);
    }

    private static string Token(string role, IEnumerable<string> permisos)
    {
        var claims = new List<Claim>
        {
            new("sub", "13662000-0000-4000-8000-000000000001"),
            new("role", role),
            new("role_code", role),
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
}
