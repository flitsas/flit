using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using Flit.Api.Middleware;
using Flit.Tramites.Domain.Enums;
using Flit.Tramites.Domain.Repositories;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace Flit.Admin.Tests.Authorization;

/// <summary>
/// HU #13664 (Feature #13636 F-F, Épica #12753) — «Ver por familia» en UN solo punto (<see cref="FamilyReadEnforcementMiddleware"/>):
/// con <c>Security:ModulePermissionEnforcement:Enabled=true</c>, toda ruta por id de trámite responde 403
/// <c>family_permission_denied</c> si el usuario no tiene el «.read» de la familia del trámite (aquí un TRASPASO, por un lookup
/// de prueba), y con el flag apagado no cambia nada. El bloque estructural recorre los endpoints REALES registrados y exige que
/// toda ruta por id de trámite esté cubierta (o exenta con motivo).
/// <para>Uso de ejemplo: <c>GET /api/v1/tramites/instances/{id}/status-history</c> con <c>matriculas.read</c> pero sin <c>traspasos.read</c> → 403.</para>
/// </summary>
public sealed class FamilyReadEnforcementTests : IClassFixture<FamilyReadEnforcementTests.Hosts>
{
    private static readonly Guid Tenant = Guid.Parse("0c000000-0000-4000-8000-000000013664");
    private static readonly Guid Tramite = Guid.Parse("0c000000-0000-4000-8000-0000000136a1");
    private readonly Hosts _hosts;

    public FamilyReadEnforcementTests(Hosts hosts) => _hosts = hosts;

    /// <summary>Hosts con el lookup sustituido: cualquier id es un trámite de TRASPASO (no depende de la base).</summary>
    public sealed class Hosts : IDisposable
    {
        private readonly WebApplicationFactory<Program> _root = new();

        public WebApplicationFactory<Program> Apagado { get; }

        public WebApplicationFactory<Program> Encendido { get; }

        public Hosts()
        {
            Apagado = _root.WithWebHostBuilder(b => b.ConfigureServices(s => s.AddScoped<IProcedureInstanceFamilyLookup>(_ => new TraspasoLookup())));
            Encendido = _root.WithWebHostBuilder(b =>
            {
                b.UseSetting("Security:ModulePermissionEnforcement:Enabled", "true");
                b.ConfigureServices(s => s.AddScoped<IProcedureInstanceFamilyLookup>(_ => new TraspasoLookup()));
            });
        }

        public void Dispose()
        {
            Encendido.Dispose();
            Apagado.Dispose();
            _root.Dispose();
        }
    }

    private sealed class TraspasoLookup : IProcedureInstanceFamilyLookup
    {
        public Task<ProcedureFamily?> GetFamilyAsync(Guid procedureInstanceId, IReadOnlyCollection<Guid>? tenantIds, CancellationToken ct = default) =>
            Task.FromResult<ProcedureFamily?>(ProcedureFamily.Traspaso);

        public Task<IReadOnlyDictionary<Guid, ProcedureFamily>> GetFamiliesAsync(
            IReadOnlyCollection<Guid> procedureInstanceIds, IReadOnlyCollection<Guid>? tenantIds, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyDictionary<Guid, ProcedureFamily>>(procedureInstanceIds.ToDictionary(i => i, _ => ProcedureFamily.Traspaso));
    }

    // Muestra representativa: documentos (adjuntos), eventos (historial/notificaciones), participantes, wizard, transición, submit, prenda.
    public static TheoryData<string> RutasPorId() => new()
    {
        "GET /api/v1/tramites/instances/{id}",
        "GET /api/v1/tramites/instances/{id}/attachments",
        "GET /api/v1/tramites/instances/{id}/status-history",
        "GET /api/v1/tramites/instances/{id}/notification-dispatches",
        "GET /api/v1/tramites/instances/{id}/participants",
        "GET /api/v1/tramites/instances/{id}/wizard",
        "GET /api/v1/tramites/instances/{id}/prenda",
        "POST /api/v1/tramites/instances/{id}/transition",
        "POST /api/v1/tramites/instances/{id}/submit",
        "POST /api/v1/tramites/instances/{id}/finalize-draft",
        "GET /api/v1/tramites/{id}/document-requirements",
    };

    [Theory]
    [MemberData(nameof(RutasPorId))]
    public async Task FlagEncendido_SinElReadDeLaFamilia_403FamilyPermissionDenied(string firma)
    {
        var response = await Llamar(_hosts.Encendido, firma, Token("matriculas.read", "otros-tramites.read", "traspasos.create"));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden, firma);
        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).Should().Contain("family_permission_denied", firma);
    }

    [Theory]
    [MemberData(nameof(RutasPorId))]
    public async Task FlagEncendido_ConElReadDeLaFamilia_NoRecibe403(string firma)
    {
        var response = await Llamar(_hosts.Encendido, firma, Token("traspasos.read"));

        response.StatusCode.Should().NotBe(HttpStatusCode.Forbidden, firma).And.NotBe(HttpStatusCode.Unauthorized, firma);
    }

    [Theory]
    [MemberData(nameof(RutasPorId))]
    public async Task FlagApagado_SinPermisosPorFamilia_NoRecibe403(string firma)
    {
        var response = await Llamar(_hosts.Apagado, firma, Token());

        response.StatusCode.Should().NotBe(HttpStatusCode.Forbidden, firma).And.NotBe(HttpStatusCode.Unauthorized, firma);
    }

    // Red: el detalle y los documentos de la red también se guardan (el .read de la familia manda antes que la policy de cabeza).
    [Theory]
    [InlineData("GET /api/v1/tramites/network/instances/{id}")]
    [InlineData("GET /api/v1/tramites/network/instances/{id}/attachments")]
    public async Task Red_FlagEncendido_SinElReadDeLaFamilia_403FamilyPermissionDenied(string firma)
    {
        var response = await Llamar(_hosts.Encendido, firma, Token("matriculas.read", "tramites.read"));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden, firma);
        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).Should().Contain("family_permission_denied", firma);
    }

    [Fact]
    public async Task PauseMassive_FlagEncendido_ConUnTramiteDeFamiliaSinRead_403()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/tramites/instances/pause-massive")
        {
            Content = JsonContent.Create(new { ids = new[] { Tramite }, paused = true, observation = "x" }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token("matriculas.read"));
        request.Headers.Add("X-Tenant-Id", Tenant.ToString());

        var response = await _hosts.Encendido.CreateClient().SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).Should().Contain("family_permission_denied");
    }

    // Cobertura estructural: ninguna ruta por id de trámite puede quedar sin el guard (una ruta nueva sin cubrir hace fallar esto).
    [Fact]
    public void TodaRutaPorIdDeTramite_EstaCubierta_OExentaConMotivo()
    {
        var rutas = _hosts.Encendido.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(FamilyReadEnforcementMiddleware.IsProcedureByIdRoute)
            .ToList();

        rutas.Count.Should().BeGreaterThan(60, "el recorrido tiene que ver las ~69 rutas de /instances/{id…}");
        var sinCubrir = rutas
            .Where(r => !FamilyReadEnforcementMiddleware.Covers(r) && !FamilyReadEnforcementMiddleware.Exempt.ContainsKey(r.RoutePattern.RawText!))
            .Select(r => r.RoutePattern.RawText);
        sinCubrir.Should().BeEmpty();
        FamilyReadEnforcementMiddleware.Exempt.Where(e => string.IsNullOrWhiteSpace(e.Value)).Should().BeEmpty("toda exención lleva su motivo");
    }

    // Y al revés: ninguna ruta bajo /tramites/instances/{ o /network/instances/{ con un parámetro distinto de «id» se escapa del patrón.
    [Fact]
    public void TodaRutaBajoInstances_LlevaElIdComoParametroConocido()
    {
        var rutas = _hosts.Encendido.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Select(r => r.RoutePattern.RawText!)
            .Where(t => t.StartsWith("/api/v1/tramites/instances/{", StringComparison.OrdinalIgnoreCase)
                || t.StartsWith("/api/v1/tramites/network/instances/{", StringComparison.OrdinalIgnoreCase));

        rutas.Where(t => !t.Contains("/instances/{id", StringComparison.OrdinalIgnoreCase)).Should().BeEmpty();
    }

    private static async Task<HttpResponseMessage> Llamar(WebApplicationFactory<Program> factory, string firma, string token)
    {
        var partes = firma.Split(' ', 2);
        var ruta = partes[1].Replace("{id}", Tramite.ToString(), StringComparison.Ordinal);
        using var request = new HttpRequestMessage(new HttpMethod(partes[0]), ruta);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add("X-Tenant-Id", Tenant.ToString());
        if (partes[0] != "GET")
            request.Content = JsonContent.Create(new { toStatus = "entregado", reason = "x" });
        return await factory.CreateClient().SendAsync(request, TestContext.Current.CancellationToken);
    }

    private static string Token(params string[] permisos)
    {
        var claims = new List<Claim>
        {
            new("sub", "13664000-0000-4000-8000-000000000001"),
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
}
