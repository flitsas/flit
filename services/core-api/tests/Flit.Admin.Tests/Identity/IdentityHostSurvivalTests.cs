using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Flit.Api.Hosting;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Identity;
using Flit.Infrastructure.Persistence.Entities.Platform;
using Flit.Infrastructure.Persistence.Entities.Security;
using Flit.Modules.Security.Domain.Auth;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Flit.Admin.Tests.Identity;

/// <summary>
/// HU #13226 (Epic #13217) — core-identity y core-api sobre la misma base, sin llamarse entre sí: el login completo
/// funciona solo con core-identity, sus tokens sirven en core-api y cerrar sesión en uno corta en el otro. Ver
/// <c>docs/suite/identidad-frontera.md</c> §5.
/// </summary>
public sealed class IdentityHostSurvivalTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private const string Password = "IdentityPass1!";

    private readonly WebApplicationFactory<Program> _api;
    private readonly WebApplicationFactory<Program> _identity;
    private readonly string _suffix = Guid.NewGuid().ToString("N")[..10];
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _roleId = Guid.NewGuid();
    private string Email => $"identity-{_suffix}@flit.local";

    public IdentityHostSurvivalTests(WebApplicationFactory<Program> factory)
    {
        _api = OidcServerTests.WithOidc(factory).WithWebHostBuilder(b =>
        {
            b.UseSetting("Jwt:PersistSigningKey", "true");
            b.UseSetting("Jwt:ValidateIssuedTokens", "true");
        });
        _identity = _api.WithWebHostBuilder(b => b.UseSetting(HostRoles.ConfigKey, "identity"));
        SeedAsync().GetAwaiter().GetResult();
    }

    [Fact]
    public async Task SoloConCoreIdentity_ElLoginCompletoFunciona()
    {
        // Solo se usa la fábrica de core-identity: si dependiera de core-api por la red, no tendría a quién llamar.
        var ct = TestContext.Current.CancellationToken;
        var browser = await LoggedInAsync(_identity, ct);

        var tokens = await OidcServerTests.CodeFlowAsync(browser, "tramites", ct);
        var refreshed = await OidcServerTests.RefreshAsync(browser, tokens.GetProperty("refresh_token").GetString()!, ct);
        var legacy = await Client(_identity).PostAsJsonAsync("/api/v1/auth/login", new { email = Email, password = Password }, ct);

        tokens.GetProperty("access_token").GetString().Should().NotBeNullOrEmpty();
        refreshed.StatusCode.Should().Be(HttpStatusCode.OK);
        legacy.StatusCode.Should().Be(HttpStatusCode.OK, "el login de siempre también vive en core-identity");
    }

    [Fact]
    public async Task TokenOidcDeCoreIdentity_SirveEnCoreApi()
    {
        var ct = TestContext.Current.CancellationToken;
        var accessToken = (await OidcServerTests.CodeFlowAsync(await LoggedInAsync(_identity, ct), "tramites", ct))
            .GetProperty("access_token").GetString()!;

        var me = await MeOnApiAsync(accessToken, ct);

        me.StatusCode.Should().Be(HttpStatusCode.OK, await me.Content.ReadAsStringAsync(ct));
    }

    [Fact]
    public async Task TokenDelLoginDeSiempreEnCoreIdentity_SirveEnCoreApi()
    {
        var ct = TestContext.Current.CancellationToken;
        var login = await Client(_identity).PostAsJsonAsync("/api/v1/auth/login", new { email = Email, password = Password }, ct);
        var accessToken = (await login.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("accessToken").GetString()!;

        var me = await MeOnApiAsync(accessToken, ct);

        me.StatusCode.Should().Be(HttpStatusCode.OK, await me.Content.ReadAsStringAsync(ct));
    }

    [Fact]
    public async Task CerrarSesionEnCoreIdentity_CoreApiRechazaElToken()
    {
        var ct = TestContext.Current.CancellationToken;
        var browser = await LoggedInAsync(_identity, ct);
        var accessToken = (await OidcServerTests.CodeFlowAsync(browser, "tramites", ct)).GetProperty("access_token").GetString()!;

        // Sin consultar antes a core-api con este token: guarda 15 s la validez de cada sesión.
        await browser.GetAsync($"/connect/logout?client_id=tramites&post_logout_redirect_uri={Uri.EscapeDataString("https://dev.tramites.flitsas.online/")}", ct);
        var me = await MeOnApiAsync(accessToken, ct);

        me.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await me.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("code").GetString().Should().Be("SESSION_EXPIRED");
    }

    [Fact]
    public async Task LaSesionDelHub_SirveEnLosDosProcesos()
    {
        // Durante el cambio de la bandera del gateway, una sesión del hub abierta en uno sigue sirviendo en el otro:
        // la cookie flit_hub se cifra con el anillo de Data Protection que vive en la base.
        var ct = TestContext.Current.CancellationToken;
        var cookie = await HubCookieAsync(ct); // sesión del hub abierta en core-api

        var other = Client(_identity);
        other.DefaultRequestHeaders.Add("Cookie", cookie);
        var tokens = await OidcServerTests.CodeFlowAsync(other, "tramites", ct);

        tokens.GetProperty("access_token").GetString().Should().NotBeNullOrEmpty();
    }

    private async Task<string> HubCookieAsync(CancellationToken ct)
    {
        var login = await Client(_api).PostAsJsonAsync("/connect/login", new { email = Email, password = Password }, ct);
        login.StatusCode.Should().Be(HttpStatusCode.OK);
        return string.Join("; ", login.Headers.GetValues("Set-Cookie").Select(c => c.Split(';')[0]));
    }

    private static HttpClient Client(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = false, BaseAddress = new Uri("https://localhost") });
        client.DefaultRequestHeaders.Add("X-Flit-Domain", OidcServerTests.Hub);
        return client;
    }

    private async Task<HttpClient> LoggedInAsync(WebApplicationFactory<Program> factory, CancellationToken ct)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true, BaseAddress = new Uri("https://localhost") });
        client.DefaultRequestHeaders.Add("X-Flit-Domain", OidcServerTests.Hub);
        (await client.PostAsJsonAsync("/connect/login", new { email = Email, password = Password }, ct)).StatusCode.Should().Be(HttpStatusCode.OK);
        return client;
    }

    private async Task<HttpResponseMessage> MeOnApiAsync(string accessToken, CancellationToken ct)
    {
        var api = Client(_api);
        api.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return await api.GetAsync("/api/v1/auth/me", ct);
    }

    private async Task SeedAsync()
    {
        using var scope = _api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FlitDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var now = DateTimeOffset.UtcNow;
        db.Tenants.Add(new Tenant { Id = _tenantId, Code = $"IT-IDH-{_suffix}", LegalName = "Empresa core-identity", TaxId = TestNit.Unique(), TenantType = "RENTING", IsActive = true, CreatedAt = now });
        db.Users.Add(new User { Id = _userId, Email = Email, DisplayName = "core-identity", Status = "active", HomeTenantId = _tenantId, CreatedAt = now });
        db.UserCredentials.Add(new UserCredential { Id = Guid.CreateVersion7(), UserId = _userId, PasswordHash = hasher.Hash(Password), CreatedAt = now });
        db.Roles.Add(new Role { Id = _roleId, Code = $"IdhTram-{_suffix}", Name = "Rol Trámites core-identity", TargetEntityType = "COMPANY", ProductCode = "tramites", IsActive = true, CreatedAt = now });
        await db.SaveChangesAsync();
        db.UserRoleAssignments.Add(new UserRoleAssignment { Id = Guid.CreateVersion7(), TenantId = _tenantId, UserId = _userId, RoleId = _roleId, AssignedAt = now, CreatedAt = now });
        await db.SaveChangesAsync();
    }

    public void Dispose()
    {
        using var scope = _api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FlitDbContext>();
        db.Database.ExecuteSql($"DELETE FROM identity.oidc_tokens WHERE subject = {_userId.ToString()}");
        db.Database.ExecuteSql($"DELETE FROM identity.oidc_authorizations WHERE subject = {_userId.ToString()}");
        db.UserRoleAssignments.Where(a => a.UserId == _userId).ExecuteDelete();
        db.Roles.Where(r => r.Id == _roleId).ExecuteDelete();
        db.UserCredentials.Where(c => c.UserId == _userId).ExecuteDelete();
        db.Set<TenantProductEntity>().Where(r => r.TenantId == _tenantId).ExecuteDelete();
        db.TenantConfigAuditLogs.Where(a => a.TenantId == _tenantId).ExecuteDelete();
        db.Users.Where(u => u.Id == _userId).ExecuteDelete();
        db.Tenants.Where(t => t.Id == _tenantId).ExecuteDelete();
    }
}
