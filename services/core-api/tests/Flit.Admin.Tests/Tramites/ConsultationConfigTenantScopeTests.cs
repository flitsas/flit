using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using Flit.Tramites.Application.UseCases.Consultations;
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
/// Bug (registro diferido 2026-09-15, ver <c>.claude/state/pending-work-items/2026-09-15-consultation-config-x-tenant-id-crudo.md</c>)
/// — <c>GET /api/v1/tramites/consultation-config</c> confiaba en el <c>X-Tenant-Id</c> crudo del cliente:
/// la ruta estaba congelada como deuda en <c>LegacyUncoveredRoutes</c> (HU #12320) y excluida del
/// barrido AC9 (Bug #12558), fuera de
/// <see cref="Flit.Api.Middleware.TenantEnforcementMiddleware.RuntimeScopedRoutes"/>. Un usuario
/// NO-SuperAdmin del tenant A podía mandar <c>X-Tenant-Id: B</c> y ver el proveedor primario de consulta
/// (VIN / placa / conductor) y los flags <c>OnlyOwnVehicles*</c> / <c>BlockProcedureFamily*</c> de B.
/// <para>
/// Prueba NEGATIVA (mismo patrón que <c>AdminTramitesTenantScopeTests</c> y
/// <c>UserUiPreferencesTenantScopeTests</c>): la única dependencia del handler que recibe el tenant
/// (<see cref="IConsultationTenantOverrideProvider"/>) se sustituye con NSubstitute y se verifica CON
/// QUÉ <c>tenantId</c> se invoca — antes del fix, con el header crudo (B, el tenant ajeno); después del
/// fix, el <c>TenantEnforcementMiddleware</c> sobrescribe el header con el tenant del JWT (A) para
/// cualquier caller NO-SuperAdmin, así que el proveedor solo ve A. El endpoint no cambia (sigue leyendo
/// <c>[FromHeader]</c>), exactamente igual que el fix de <c>/api/v1/me/ui-preferences</c>.
/// </para>
/// Caso SuperAdmin + header B: sin regresión — el middleware respeta el header que manda para acotar,
/// así que el proveedor debe ver B. Caso company-user SIN header: el middleware inyecta el tenant del
/// JWT (A), donde antes el endpoint respondía 400 «Falta header X-Tenant-Id».
/// Sin PostgreSQL: el proveedor sustituido intercepta antes de tocar cualquier motor de persistencia real.
/// Uso de ejemplo: no se invoca; corre en CI con el filtro <c>FullyQualifiedName~ConsultationConfigTenantScope</c>.
/// </summary>
public sealed class ConsultationConfigTenantScopeTests : IClassFixture<ConsultationConfigTenantScopeTests.ProviderSubstituteFactory>
{
    /// <summary>Tenant real del usuario de compañía (dueño del JWT).</summary>
    private static readonly Guid TenantA = Guid.Parse("d0000000-0000-4000-8000-0000000000a1");

    /// <summary>Tenant ajeno que el atacante manda crudo en <c>X-Tenant-Id</c>.</summary>
    private static readonly Guid TenantB = Guid.Parse("d0000000-0000-4000-8000-0000000000b2");

    /// <summary>Tenant que acota el SuperAdmin (mismo valor que <see cref="TenantB"/> a propósito: el
    /// SuperAdmin SÍ debe poder leer la configuración de esa compañía si la acota).</summary>
    private static readonly Guid SuperAdminScopedTenant = TenantB;

    private static readonly Guid UserId = Guid.Parse("d0000000-0001-4000-8000-000000000003");

    private const string Route = "/api/v1/tramites/consultation-config";

    private readonly ProviderSubstituteFactory _factory;

    public ConsultationConfigTenantScopeTests(ProviderSubstituteFactory factory)
    {
        _factory = factory;
        _factory.Provider.ClearReceivedCalls();
        // null ⇒ defaults globales (contrato del proveedor): el handler responde 200 con la cadena por defecto.
        _factory.Provider.GetAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ConsultationTenantOverride?>(null));
    }

    // ── No-SuperAdmin: el tenant SIEMPRE sale del JWT, nunca del header ──────────────────────

    [Fact]
    public async Task Get_NoSuperAdmin_ConHeaderDeOtroTenant_InvocaElProveedorConElTenantDelJwt()
    {
        var client = ClientFor(TenantA, headerTenant: TenantB, role: "AdminCompany");

        var response = await client.GetAsync(Route, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        await _factory.Provider.Received(1).GetAsync(TenantA, Arg.Any<CancellationToken>());
        await _factory.Provider.DidNotReceive().GetAsync(TenantB, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Get_NoSuperAdmin_SinHeader_InvocaElProveedorConElTenantDelJwt()
    {
        var client = ClientFor(TenantA, headerTenant: null, role: "AdminCompany");

        var response = await client.GetAsync(Route, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "el middleware inyecta el tenant del JWT en el header, así que el endpoint ya no responde 400 por header ausente");
        await _factory.Provider.Received(1).GetAsync(TenantA, Arg.Any<CancellationToken>());
    }

    // ── SuperAdmin + header: sin regresión, sigue acotando con el header ─────────────────────

    [Fact]
    public async Task Get_SuperAdmin_ConHeader_InvocaElProveedorConElTenantDelHeader()
    {
        var client = ClientFor(tenantOfJwt: null, headerTenant: SuperAdminScopedTenant, role: "SuperAdmin");

        var response = await client.GetAsync(Route, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        await _factory.Provider.Received(1).GetAsync(SuperAdminScopedTenant, Arg.Any<CancellationToken>());
    }

    // ── helpers ──────────────────────────────────────────────────────────────────────────────

    private HttpClient ClientFor(Guid? tenantOfJwt, Guid? headerTenant, string role)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token(tenantOfJwt, role));
        if (headerTenant is { } h)
            client.DefaultRequestHeaders.Add("X-Tenant-Id", h.ToString());
        return client;
    }

    /// <summary>JWT firmado igual que <c>AdminTramitesTenantScopeTests.Token</c> (mismo host de pruebas,
    /// misma llave simétrica configurada para el entorno de test).</summary>
    private static string Token(Guid? tenantId, string role)
    {
        var claims = new List<Claim>
        {
            new("sub", UserId.ToString()),
            new("role", role),
            new("role_code", role),
        };
        if (tenantId is { } t)
            claims.Add(new Claim("tenant_id", t.ToString()));

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

    /// <summary>Host con <see cref="IConsultationTenantOverrideProvider"/> sustituido (NSubstitute).</summary>
    public sealed class ProviderSubstituteFactory : WebApplicationFactory<Program>
    {
        public IConsultationTenantOverrideProvider Provider { get; } = Substitute.For<IConsultationTenantOverrideProvider>();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            ArgumentNullException.ThrowIfNull(builder);
            builder.ConfigureTestServices(services =>
            {
                services.AddScoped(_ => Provider);
            });
        }
    }
}
