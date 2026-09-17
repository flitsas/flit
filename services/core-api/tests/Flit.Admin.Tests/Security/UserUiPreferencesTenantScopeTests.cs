using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using Flit.Modules.Security.Domain.UiPreferences;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using NSubstitute;
using Xunit;

namespace Flit.Admin.Tests.Security;

/// <summary>
/// Bug #12558 — <c>/api/v1/me/ui-preferences/{scope}</c> confiaba en el <c>X-Tenant-Id</c> crudo del
/// cliente (fuera de <see cref="Flit.Api.Middleware.TenantEnforcementMiddleware.RuntimeScopedRoutes"/>):
/// un usuario NO-SuperAdmin del tenant A podía mandar <c>X-Tenant-Id: B</c> y leer/escribir la
/// preferencia de UI guardada bajo el tenant B. El <c>user_id</c> ya salía del JWT (correcto, sin
/// cambios); solo el tenant era el problema.
/// <para>
/// Prueba NEGATIVA (mismo patrón que <c>AdminTramitesTenantScopeTests</c>): el único repositorio del
/// dominio (<see cref="IUserUiPreferenceRepository"/>) se sustituye con NSubstitute y se verifica CON
/// QUÉ <c>tenantId</c> se invoca — antes del fix, con el header crudo (B, el tenant ajeno); después del
/// fix, el <c>TenantEnforcementMiddleware</c> sobrescribe el header con el tenant del JWT (A) para
/// cualquier caller NO-SuperAdmin, así que el repositorio solo ve A.
/// </para>
/// Caso SuperAdmin + header B: sin regresión — el middleware respeta el header que manda para acotar,
/// así que el repositorio debe ver B (nunca el tenant propio del SuperAdmin, que aquí ni se manda).
/// Sin PostgreSQL: el repo sustituido intercepta antes de tocar cualquier motor de persistencia real.
/// </summary>
public sealed class UserUiPreferencesTenantScopeTests : IClassFixture<UserUiPreferencesTenantScopeTests.RepoSubstituteFactory>
{
    /// <summary>Tenant real del usuario de compañía (dueño del JWT).</summary>
    private static readonly Guid TenantA = Guid.Parse("c0000000-0000-4000-8000-0000000000a1");

    /// <summary>Tenant ajeno que el atacante manda crudo en <c>X-Tenant-Id</c>.</summary>
    private static readonly Guid TenantB = Guid.Parse("c0000000-0000-4000-8000-0000000000b2");

    /// <summary>Tenant que acota el SuperAdmin (mismo valor que <see cref="TenantB"/> a propósito: el
    /// SuperAdmin SÍ debe poder leer/escribir la preferencia de esa compañía si la acota).</summary>
    private static readonly Guid SuperAdminScopedTenant = TenantB;

    private static readonly Guid UserId = Guid.Parse("c0000000-0001-4000-8000-000000000003");

    private const string Scope = UiPreferenceScopes.TramitesColumns;

    private readonly RepoSubstituteFactory _factory;

    public UserUiPreferencesTenantScopeTests(RepoSubstituteFactory factory)
    {
        _factory = factory;
        _factory.Repo.ClearReceivedCalls();
        _factory.Repo.UpsertAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ci => Task.FromResult(new UserUiPreference
            {
                TenantId = (Guid)ci[0],
                UserId = (Guid)ci[1],
                Scope = (string)ci[2],
                ValueJson = (string)ci[3],
            }));
    }

    // ── No-SuperAdmin: el tenant SIEMPRE sale del JWT, nunca del header ──────────────────────

    [Fact]
    public async Task Get_NoSuperAdmin_ConHeaderDeOtroTenant_InvocaElRepositorioConElTenantDelJwt()
    {
        var client = ClientFor(TenantA, headerTenant: TenantB, role: "AdminCompany");

        await client.GetAsync($"/api/v1/me/ui-preferences/{Scope}", TestContext.Current.CancellationToken);

        await _factory.Repo.Received(1).FindAsync(TenantA, UserId, Scope, Arg.Any<CancellationToken>());
        await _factory.Repo.DidNotReceive().FindAsync(TenantB, Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Put_NoSuperAdmin_ConHeaderDeOtroTenant_InvocaElRepositorioConElTenantDelJwt()
    {
        var client = ClientFor(TenantA, headerTenant: TenantB, role: "AdminCompany");

        await client.PutAsJsonAsync($"/api/v1/me/ui-preferences/{Scope}", new { value = new { columns = new[] { "placa" } } },
            TestContext.Current.CancellationToken);

        await _factory.Repo.Received(1).UpsertAsync(TenantA, UserId, Scope, Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _factory.Repo.DidNotReceive().UpsertAsync(TenantB, Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    // ── SuperAdmin + header: sin regresión, sigue acotando con el header ─────────────────────

    [Fact]
    public async Task Get_SuperAdmin_ConHeader_InvocaElRepositorioConElTenantDelHeader()
    {
        var client = ClientFor(tenantOfJwt: null, headerTenant: SuperAdminScopedTenant, role: "SuperAdmin");

        await client.GetAsync($"/api/v1/me/ui-preferences/{Scope}", TestContext.Current.CancellationToken);

        await _factory.Repo.Received(1).FindAsync(SuperAdminScopedTenant, UserId, Scope, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Put_SuperAdmin_ConHeader_InvocaElRepositorioConElTenantDelHeader()
    {
        var client = ClientFor(tenantOfJwt: null, headerTenant: SuperAdminScopedTenant, role: "SuperAdmin");

        await client.PutAsJsonAsync($"/api/v1/me/ui-preferences/{Scope}", new { value = new { columns = new[] { "placa" } } },
            TestContext.Current.CancellationToken);

        await _factory.Repo.Received(1).UpsertAsync(SuperAdminScopedTenant, UserId, Scope, Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    // ── helpers ──────────────────────────────────────────────────────────────────────────────

    private HttpClient ClientFor(Guid? tenantOfJwt, Guid headerTenant, string role)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token(tenantOfJwt, role));
        client.DefaultRequestHeaders.Add("X-Tenant-Id", headerTenant.ToString());
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

    /// <summary>Host con <see cref="IUserUiPreferenceRepository"/> sustituido (NSubstitute).</summary>
    public sealed class RepoSubstituteFactory : WebApplicationFactory<Program>
    {
        public IUserUiPreferenceRepository Repo { get; } = Substitute.For<IUserUiPreferenceRepository>();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            ArgumentNullException.ThrowIfNull(builder);
            builder.ConfigureTestServices(services =>
            {
                services.AddScoped(_ => Repo);
            });
        }
    }
}
