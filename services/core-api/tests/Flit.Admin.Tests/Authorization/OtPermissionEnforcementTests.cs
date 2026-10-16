using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.RegularExpressions;
using Flit.Api.Authorization;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace Flit.Admin.Tests.Authorization;

/// <summary>
/// HU #13639 (Feature #13100 FA-3/FA-4, Épica #12753) — exigencia del slug OT del Anexo A por ruta, detrás del flag
/// <c>Security:OtPermissionEnforcement:Enabled</c> (apagado por defecto, P2). Las rutas salen de
/// <see cref="OtPermissionMap"/> (90 = Parte 2 del Anexo). Las rutas pueden responder 400/404 por datos: lo que
/// importa es que no pasen a 401/403.
/// <para>Uso de ejemplo: un Theory con MemberData(Rutas) llama cada ruta con el flag encendido.</para>
/// </summary>
public sealed class OtPermissionEnforcementTests : IClassFixture<OtPermissionEnforcementTests.Hosts>
{
    private static readonly string[] SlugsDock =
        [OtPermissionMap.Tramites, OtPermissionMap.Improntas, OtPermissionMap.Reportes, OtPermissionMap.Mandatos];

    private static readonly string[] TodosLosSlugs = [.. OtPermissionMap.BySignature.Values.Distinct()];
    private static readonly Guid TenantOt = Guid.Parse("0e000000-0000-4000-8000-000000013639");

    private readonly WebApplicationFactory<Program> _apagado;
    private readonly WebApplicationFactory<Program> _encendido;

    public OtPermissionEnforcementTests(Hosts hosts)
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
            Encendido = Apagado.WithWebHostBuilder(b => b.UseSetting("Security:OtPermissionEnforcement:Enabled", "true"));

        public void Dispose()
        {
            Encendido.Dispose();
            Apagado.Dispose();
        }
    }

    public static TheoryData<string> Rutas() => Datos(_ => true);

    public static TheoryData<string> RutasDock() => Datos(slug => SlugsDock.Contains(slug));

    public static TheoryData<string> RutasUsuariosEIntegracion() =>
        Datos(slug => slug is OtPermissionMap.Usuarios or OtPermissionMap.Integracion);

    private static TheoryData<string> Datos(Func<string, bool> slugFilter)
    {
        var data = new TheoryData<string>();
        foreach (var kv in OtPermissionMap.BySignature.Where(kv => slugFilter(kv.Value)))
        {
            data.Add(kv.Key);
        }

        return data;
    }

    // ── Cobertura: el mapa es el inventario del Anexo y apunta a rutas reales ──────────────────

    [Fact]
    public void Mapa_Tiene90RutasConElConteoDelAnexoPorSlug()
    {
        OtPermissionMap.BySignature.Should().HaveCount(90);
        OtPermissionMap.BySignature.GroupBy(kv => kv.Value).ToDictionary(g => g.Key, g => g.Count()).Should().BeEquivalentTo(
            new Dictionary<string, int>
            {
                [OtPermissionMap.Tramites] = 29, [OtPermissionMap.Reportes] = 23, [OtPermissionMap.Mandatos] = 11,
                [OtPermissionMap.Mandatarios] = 11, [OtPermissionMap.Usuarios] = 6, [OtPermissionMap.Improntas] = 4,
                [OtPermissionMap.Integracion] = 4, [OtPermissionMap.Documentos] = 2,
            });
    }

    [Fact]
    public void Mapa_CadaFirmaCorrespondeAUnaRutaRegistrada_YLasRutasOtSinSlugSonLas33DelAnexo()
    {
        var registradas = _apagado.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .SelectMany(e => (e.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods ?? [])
                .Select(m => (Firma: OtPermissionMap.Signature(m, e.RoutePattern.RawText), Endpoint: e)))
            .ToList();

        OtPermissionMap.BySignature.Keys.Except(registradas.Select(r => r.Firma)).Should().BeEmpty("cada ruta del mapa debe existir");

        var conPolicyOt = registradas
            .Where(r => r.Endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>().Any(a =>
                a.Policy == AdminAuthorization.OtModulePolicy || a.Policy == AdminAuthorization.OtAdminOrSuperAdminPolicy))
            .Select(r => r.Firma).Distinct().ToList();
        conPolicyOt.Should().HaveCount(123);
        conPolicyOt.Except(OtPermissionMap.BySignature.Keys).Should().HaveCount(33);
    }

    // ── AC5: flag apagado (default) → solo política de rol, como hoy ────────────────────────────

    [Fact]
    public void AC5_FlagApagadoPorDefecto()
    {
        _apagado.Services.GetRequiredService<IOptions<OtPermissionEnforcementOptions>>().Value.Enabled.Should().BeFalse();
    }

    [Theory]
    [MemberData(nameof(Rutas))]
    public async Task AC5_FlagApagado_OtAdminSinSlugs_NoRecibe403PorPermiso(string firma)
    {
        var response = await Llamar(_apagado, firma, Token("ot_admin", []));

        await AssertNoBloqueada(response);
    }

    // ── AC1: sin regresión por ruta (Admin OT con los 8 slugs del backfill) ──────────────────────

    [Theory]
    [MemberData(nameof(Rutas))]
    public async Task AC1_FlagEncendido_OtAdminConLosSlugs_ObtieneElMismoEstadoQueConElFlagApagado(string firma)
    {
        var antes = await Llamar(_apagado, firma, Token("ot_admin", []));
        var despues = await Llamar(_encendido, firma, Token("ot_admin", TodosLosSlugs));

        await AssertNoBloqueada(despues);
        despues.StatusCode.Should().Be(antes.StatusCode, firma);
    }

    // ── AC2: cumple OtModule pero sin el slug en su claim → 403 ─────────────────────────────────

    [Theory]
    [MemberData(nameof(Rutas))]
    public async Task AC2_FlagEncendido_OtAdminSinElSlug_403ConCodigoYSlug(string firma)
    {
        var response = await Llamar(_encendido, firma, Token("ot_admin", []));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden, firma);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        body.Should().Contain("OT_PERMISSION_REQUIRED").And.Contain(OtPermissionMap.BySignature[firma]);
    }

    [Theory]
    [MemberData(nameof(Rutas))]
    public async Task AC2_FlagEncendido_SlugDeOtroModulo_NoAbreLaRuta(string firma)
    {
        var otro = TodosLosSlugs.First(s => s != OtPermissionMap.BySignature[firma]);
        var response = await Llamar(_encendido, firma, Token("ot_admin", [otro]));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden, firma);
    }

    [Fact]
    public async Task SuperAdmin_ConservaSuBypass_ConElFlagEncendido()
    {
        foreach (var firma in new[] { "GET /api/v1/admin/ot/metrics/operational", "GET /api/v1/admin/ot/users", "GET /api/v1/admin/ot/webhooks" })
        {
            var response = await Llamar(_encendido, firma, Token("SuperAdmin", []));
            await AssertNoBloqueada(response);
        }
    }

    // ── AC3: usuarios OT no admin conservan lo que ven en el dock (slugs 1-4) ────────────────────

    [Theory]
    [MemberData(nameof(RutasDock))]
    public async Task AC3_FlagEncendido_OperadorOtConSlugsDelDock_ConservaElAcceso(string firma)
    {
        var response = await Llamar(_encendido, firma, Token("gestor_tramites_ot", SlugsDock));

        await AssertNoBloqueada(response);
    }

    // ── AC4 (P1): usuarios, invitaciones, webhooks y api-logs solo para ot_admin ─────────────────

    [Theory]
    [MemberData(nameof(RutasUsuariosEIntegracion))]
    public async Task AC4_FlagEncendido_OperadorOtSinAdmin_403_YOtAdminConserva(string firma)
    {
        var operador = await Llamar(_encendido, firma, Token("gestor_tramites_ot", SlugsDock));
        operador.StatusCode.Should().Be(HttpStatusCode.Forbidden, firma);
        (await operador.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).Should().Contain("OT_PERMISSION_REQUIRED");

        var admin = await Llamar(_encendido, firma, Token("ot_admin", TodosLosSlugs));
        await AssertNoBloqueada(admin);
    }

    // ── Soporte ──────────────────────────────────────────────────────────────────────────────────

    private static async Task AssertNoBloqueada(HttpResponseMessage response)
    {
        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
        if (response.StatusCode == HttpStatusCode.Forbidden)
        {
            (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))
                .Should().NotContain("OT_PERMISSION_REQUIRED");
        }
    }

    private static async Task<HttpResponseMessage> Llamar(WebApplicationFactory<Program> factory, string firma, string token)
    {
        var partes = firma.Split(' ', 2);
        var ruta = Regex.Replace(partes[1], @"\{\w+\}", _ => Guid.NewGuid().ToString());
        using var request = new HttpRequestMessage(new HttpMethod(partes[0]), ruta);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (partes[0] is "POST" or "PUT" or "PATCH")
        {
            request.Content = partes[1].EndsWith("/attachments", StringComparison.Ordinal)
                ? new MultipartFormDataContent()
                : new StringContent("{}", Encoding.UTF8, "application/json");
        }

        return await factory.CreateClient().SendAsync(request, TestContext.Current.CancellationToken);
    }

    private static string Token(string role, IEnumerable<string> permisos)
    {
        var claims = new List<Claim>
        {
            new("sub", "13639000-0000-4000-8000-000000000001"),
            new("role", role),
            new("role_code", role),
            new("tenant_id", TenantOt.ToString()),
        };
        if (role != "SuperAdmin")
        {
            claims.Add(new Claim("entity_type", "TRANSIT_OFFICE"));
        }

        // ot_admin ya trae consolidado-masivo.download (HU #13369): POST /consolidados/lotes lo exige aparte.
        claims.Add(new Claim("permissions", "consolidado-masivo.download"));
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
