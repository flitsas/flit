using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Web;
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
using Microsoft.IdentityModel.JsonWebTokens;
using Xunit;

namespace Flit.Admin.Tests.Identity;

/// <summary>
/// HU #12992 (FLIT Suite A-07) — token por producto (contrato §2): roles y permisos del producto (más los de plataforma
/// hasta B-12), sin token si
/// el producto está apagado o el usuario no tiene rol, refresh rotado que se relee de la base, SuperAdmin con bypass,
/// y core-api aceptando el token del hub.
/// </summary>
public sealed class OidcProductTokenTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private const string Password = "OidcPass1!";
    private const string PlatformCallback = "https://dev.flitsas.online/auth/callback";

    private readonly WebApplicationFactory<Program> _factory;
    private readonly string _suffix = Guid.NewGuid().ToString("N")[..10];
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _platformRoleId = Guid.NewGuid();
    private readonly Guid _tramitesRoleId = Guid.NewGuid();
    private readonly List<Guid> _modules = [];
    private readonly List<Guid> _actions = [];
    private string Email => $"oidc-prod-{_suffix}@flit.local";
    private string TramitesSlug => $"oidc{_suffix}.bandeja.read";
    private string PlatformSlug => $"oidcplat{_suffix}.empresa.read";

    public OidcProductTokenTests(WebApplicationFactory<Program> factory)
    {
        _factory = OidcServerTests.WithOidc(factory).WithWebHostBuilder(b =>
        {
            b.UseSetting("Jwt:PersistSigningKey", "true");
            b.UseSetting("Jwt:ValidateIssuedTokens", "true");
        });
        SeedAsync().GetAwaiter().GetResult();
    }

    private HttpClient NewClient()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true, BaseAddress = new Uri("https://localhost") });
        client.DefaultRequestHeaders.Add("X-Flit-Domain", OidcServerTests.Hub);
        return client;
    }

    private async Task<HttpClient> LoggedInAsync(CancellationToken ct)
    {
        var client = NewClient();
        (await client.PostAsJsonAsync("/connect/login", new { email = Email, password = Password }, ct)).StatusCode.Should().Be(HttpStatusCode.OK);
        return client;
    }

    [Fact]
    public async Task TokenDeTramites_RolesYPermisosDeTramites_MasLosDePlataforma_ConLosClaimsDeSiempre()
    {
        var ct = TestContext.Current.CancellationToken;
        var tokens = await OidcServerTests.CodeFlowAsync(await LoggedInAsync(ct), "tramites", ct);
        var jwt = new JsonWebToken(tokens.GetProperty("access_token").GetString());
        var payload = JsonDocument.Parse(Base64UrlDecode(jwt.EncodedPayload)).RootElement;

        payload.GetProperty("aud").GetString().Should().Be("tramites");
        payload.GetProperty("product").GetString().Should().Be("tramites");
        payload.GetProperty("tenant_id").GetString().Should().Be(_tenantId.ToString());
        payload.GetProperty("tenant_type").GetString().Should().Be("RENTING");
        payload.GetProperty("is_group_parent").ValueKind.Should().Be(JsonValueKind.False);
        payload.GetProperty("dom").GetString().Should().Be("flit");

        // Primero el rol del producto; luego, mientras la administración de plataforma viva en Trámites (hasta B-12),
        // el de plataforma, para que el administrador de la empresa conserve esas pantallas con la sesión nueva.
        payload.GetProperty("roles").ValueKind.Should().Be(JsonValueKind.Array);
        payload.GetProperty("roles").EnumerateArray().Select(r => r.GetProperty("code").GetString())
            .Should().Equal($"OidcTram-{_suffix}", $"OidcPlat-{_suffix}");
        payload.GetProperty("role_id").EnumerateArray().Select(r => r.GetString())
            .Should().Equal(_tramitesRoleId.ToString(), _platformRoleId.ToString());
        payload.GetProperty("permissions").ValueKind.Should().Be(JsonValueKind.Array);
        payload.GetProperty("permissions").EnumerateArray().Select(p => p.GetString()).Should().Equal(TramitesSlug, PlatformSlug);
    }

    [Fact]
    public async Task TokenDelHub_SoloRolesDePlataforma()
    {
        var ct = TestContext.Current.CancellationToken;
        var tokens = await OidcServerTests.CodeFlowAsync(await LoggedInAsync(ct), "plataforma", ct, PlatformCallback);
        var payload = JsonDocument.Parse(Base64UrlDecode(new JsonWebToken(tokens.GetProperty("access_token").GetString()).EncodedPayload)).RootElement;

        payload.GetProperty("aud").GetString().Should().Be("plataforma");
        payload.GetProperty("roles").EnumerateArray().Select(r => r.GetProperty("code").GetString()).Should().Equal($"OidcPlat-{_suffix}");
        payload.GetProperty("permissions").EnumerateArray().Select(p => p.GetString()).Should().Equal(PlatformSlug);
    }

    [Fact]
    public async Task ProductoApagado_NoHayCodigo_PRODUCT_NOT_ENABLED()
    {
        var ct = TestContext.Current.CancellationToken;
        await SetTramitesEnabledAsync(false);

        var error = await AuthorizeErrorAsync(await LoggedInAsync(ct), "tramites", ct);

        error.Should().Be(("access_denied", "PRODUCT_NOT_ENABLED"));
    }

    [Fact]
    public async Task SinRolEnElProducto_NoHayCodigo_PRODUCT_ROLE_REQUIRED_PeroEntraAlHub()
    {
        var ct = TestContext.Current.CancellationToken;
        await RemoveAssignmentAsync(_tramitesRoleId);
        var client = await LoggedInAsync(ct);

        (await AuthorizeErrorAsync(client, "tramites", ct)).Should().Be(("access_denied", "PRODUCT_ROLE_REQUIRED"));

        await RemoveAssignmentAsync(_platformRoleId);
        // Sin ningún rol de plataforma, el hub igual emite su token: todo usuario entra a ver sus productos.
        var hub = await OidcServerTests.CodeFlowAsync(await LoggedInAsync(ct), "plataforma", ct, PlatformCallback);
        hub.GetProperty("access_token").GetString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task RefreshUsadoDosVeces_SeRechazaYRevocaLaCadena()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = await LoggedInAsync(ct);
        var first = (await OidcServerTests.CodeFlowAsync(client, "tramites", ct)).GetProperty("refresh_token").GetString()!;

        var rotated = await OidcServerTests.RefreshAsync(client, first, ct);
        rotated.StatusCode.Should().Be(HttpStatusCode.OK);
        var second = (await rotated.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("refresh_token").GetString()!;
        second.Should().NotBe(first, "el refresh se rota en cada uso");

        (await OidcServerTests.RefreshAsync(client, first, ct)).StatusCode.Should().Be(HttpStatusCode.BadRequest, "reusar un refresh es señal de robo");
        (await OidcServerTests.RefreshAsync(client, second, ct)).StatusCode.Should().Be(HttpStatusCode.BadRequest, "el reuso revoca toda la cadena");
    }

    [Fact]
    public async Task RefreshDespuesDeQuitarElRol_SeRechaza()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = await LoggedInAsync(ct);
        var refresh = (await OidcServerTests.CodeFlowAsync(client, "tramites", ct)).GetProperty("refresh_token").GetString()!;

        await RemoveAssignmentAsync(_tramitesRoleId);
        var response = await OidcServerTests.RefreshAsync(client, refresh, ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("error_description").GetString().Should().Be("PRODUCT_ROLE_REQUIRED");
    }

    [Fact]
    public async Task CerrarSesion_RevocaLosRefreshDeEsteNavegador_NoLosDeOtroDispositivo()
    {
        // HU #13004 (A-13): salir de un producto cierra la sesión del hub y revoca las autorizaciones de esta sesión.
        var ct = TestContext.Current.CancellationToken;
        var browser = await LoggedInAsync(ct);
        var otherDevice = await LoggedInAsync(ct);
        var refreshHere = (await OidcServerTests.CodeFlowAsync(browser, "tramites", ct)).GetProperty("refresh_token").GetString()!;
        var refreshThere = (await OidcServerTests.CodeFlowAsync(otherDevice, "tramites", ct)).GetProperty("refresh_token").GetString()!;

        var logout = await browser.GetAsync(
            $"/connect/logout?client_id=tramites&post_logout_redirect_uri={Uri.EscapeDataString("https://dev.tramites.flitsas.online/")}", ct);

        // Front-channel logout: una página que avisa a Trámites (borra su cookie) y sigue al post_logout_redirect_uri.
        logout.StatusCode.Should().Be(HttpStatusCode.OK);
        var page = await logout.Content.ReadAsStringAsync(ct);
        page.Should().Contain("https://dev.tramites.flitsas.online/auth/frontchannel-logout");
        page.Should().Contain("url=https://dev.tramites.flitsas.online/");
        logout.Headers.CacheControl!.NoStore.Should().BeTrue();
        (await OidcServerTests.RefreshAsync(browser, refreshHere, ct)).StatusCode.Should().Be(HttpStatusCode.BadRequest, "la sesión de este navegador se cerró");
        (await OidcServerTests.RefreshAsync(otherDevice, refreshThere, ct)).StatusCode.Should().Be(HttpStatusCode.OK, "otro dispositivo conserva su sesión");

        var reauthorize = await browser.GetAsync(OidcServerTests.AuthorizeUrl("tramites", "E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM"), ct);
        reauthorize.Headers.Location!.AbsolutePath.Should().Be("/login", "la sesión del hub también se cerró");
    }

    [Fact]
    public async Task CerrarSesion_SinProductosConSesion_RedirigeComoSiempre()
    {
        // HU #13004: si desde esta sesión del hub no se abrió ningún producto, no hay a quién avisar.
        var ct = TestContext.Current.CancellationToken;
        var browser = await LoggedInAsync(ct);

        var logout = await browser.GetAsync(
            $"/connect/logout?client_id=tramites&post_logout_redirect_uri={Uri.EscapeDataString("https://dev.tramites.flitsas.online/")}", ct);

        logout.StatusCode.Should().Be(HttpStatusCode.Redirect);
        logout.Headers.Location!.ToString().Should().StartWith("https://dev.tramites.flitsas.online/");
    }

    [Fact]
    public async Task CerrarSesion_AvisaACadaProductoUnaSolaVez_YLaPaginaNoEjecutaScriptsAjenos()
    {
        var ct = TestContext.Current.CancellationToken;
        var browser = await LoggedInAsync(ct);
        await OidcServerTests.CodeFlowAsync(browser, "tramites", ct);
        await OidcServerTests.CodeFlowAsync(browser, "tramites", ct);

        var logout = await browser.GetAsync(
            $"/connect/logout?client_id=tramites&post_logout_redirect_uri={Uri.EscapeDataString("https://dev.tramites.flitsas.online/")}", ct);
        var page = await logout.Content.ReadAsStringAsync(ct);

        System.Text.RegularExpressions.Regex.Matches(page, "<iframe ").Count.Should().Be(1, "un aviso por producto, aunque haya abierto sesión dos veces");
        var csp = logout.Headers.GetValues("Content-Security-Policy").Single();
        csp.Should().Contain("script-src 'nonce-").And.Contain("frame-src https://dev.tramites.flitsas.online").And.Contain("frame-ancestors 'none'");
    }

    [Fact]
    public async Task UsuarioDesactivado_PierdeElAccesoEnSuSiguienteRenovacion()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = await LoggedInAsync(ct);
        var refresh = (await OidcServerTests.CodeFlowAsync(client, "tramites", ct)).GetProperty("refresh_token").GetString()!;

        await using (var db = NewDb())
            await db.Users.Where(u => u.Id == _userId).ExecuteUpdateAsync(u => u.SetProperty(x => x.Status, "inactive"), ct);
        var response = await OidcServerTests.RefreshAsync(client, refresh, ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("error_description").GetString().Should().Be("SESSION_INVALID");
    }

    [Fact]
    public async Task SuperAdmin_EntraAUnProductoApagado_ConSuperAdminEnLosRoles()
    {
        var ct = TestContext.Current.CancellationToken;
        await SetTramitesEnabledAsync(false);
        await MakeSuperAdminAsync();

        var tokens = await OidcServerTests.CodeFlowAsync(await LoggedInAsync(ct), "tramites", ct);
        var payload = JsonDocument.Parse(Base64UrlDecode(new JsonWebToken(tokens.GetProperty("access_token").GetString()).EncodedPayload)).RootElement;

        payload.GetProperty("roles").EnumerateArray().Select(r => r.GetProperty("code").GetString()).Should().Contain("SuperAdmin");
        Values(payload.GetProperty("role")).Should().Contain("SuperAdmin");
        Values(payload.GetProperty("role_code")).Should().Contain("SuperAdmin");
    }

    [Fact]
    public async Task LaApiAceptaElTokenDelHub()
    {
        var ct = TestContext.Current.CancellationToken;
        var accessToken = (await OidcServerTests.CodeFlowAsync(await LoggedInAsync(ct), "tramites", ct)).GetProperty("access_token").GetString()!;

        var api = NewClient();
        api.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        var me = await api.GetAsync("/api/v1/auth/me", ct);

        me.StatusCode.Should().Be(HttpStatusCode.OK, await me.Content.ReadAsStringAsync(ct));
    }

    [Fact]
    public async Task CerrarSesion_ElTokenDeAccesoYaEmitidoDejaDeServir_EnOtroDispositivoSigue()
    {
        // Cierre de sesión en toda la suite: un producto abierto en este navegador pierde la sesión en su siguiente
        // llamada, sin esperar a que venza su token de 15 minutos.
        var ct = TestContext.Current.CancellationToken;
        var browser = await LoggedInAsync(ct);
        var otherDevice = await LoggedInAsync(ct);
        var tokenHere = (await OidcServerTests.CodeFlowAsync(browser, "tramites", ct)).GetProperty("access_token").GetString()!;
        var tokenThere = (await OidcServerTests.CodeFlowAsync(otherDevice, "tramites", ct)).GetProperty("access_token").GetString()!;

        async Task<HttpResponseMessage> MeAsync(string token)
        {
            var api = NewClient();
            api.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            return await api.GetAsync("/api/v1/auth/me", ct);
        }
        // Sin consultar antes con este token: la validez de la sesión se guarda 15 segundos por autorización.
        await browser.GetAsync($"/connect/logout?client_id=tramites&post_logout_redirect_uri={Uri.EscapeDataString("https://dev.tramites.flitsas.online/")}", ct);

        var after = await MeAsync(tokenHere);
        after.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await after.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("code").GetString().Should().Be("SESSION_EXPIRED");
        (await MeAsync(tokenThere)).StatusCode.Should().Be(HttpStatusCode.OK, "otro dispositivo conserva su sesión");
    }

    private static async Task<(string? Error, string? Description)> AuthorizeErrorAsync(HttpClient client, string clientId, CancellationToken ct)
    {
        var challenge = Convert.ToBase64String(SHA256.HashData(Encoding.ASCII.GetBytes("verificador-de-prueba-suficientemente-largo-123"))).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var response = await client.GetAsync(OidcServerTests.AuthorizeUrl(clientId, challenge), ct);
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        var query = HttpUtility.ParseQueryString(response.Headers.Location!.Query);
        query["code"].Should().BeNull("sin acceso no se emite código");
        return (query["error"], query["error_description"]);
    }

    /// <summary>Un claim repetido sale como arreglo; con un solo valor, como texto (igual que el JWT de siempre).</summary>
    private static IEnumerable<string?> Values(JsonElement claim) =>
        claim.ValueKind == JsonValueKind.Array ? claim.EnumerateArray().Select(v => v.GetString()) : [claim.GetString()];

    private static byte[] Base64UrlDecode(string value)
    {
        var s = value.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(s.PadRight(s.Length + ((4 - (s.Length % 4)) % 4), '='));
    }

    private FlitDbContext NewDb() => _factory.Services.CreateScope().ServiceProvider.GetRequiredService<FlitDbContext>();

    private async Task SetTramitesEnabledAsync(bool enabled)
    {
        await using var db = NewDb();
        await db.Set<TenantProductEntity>().Where(r => r.TenantId == _tenantId && r.ProductCode == "tramites")
            .ExecuteUpdateAsync(u => u.SetProperty(r => r.Enabled, enabled));
    }

    private async Task RemoveAssignmentAsync(Guid roleId)
    {
        await using var db = NewDb();
        await db.UserRoleAssignments.Where(a => a.UserId == _userId && a.RoleId == roleId)
            .ExecuteUpdateAsync(u => u.SetProperty(a => a.DeletedAt, DateTimeOffset.UtcNow));
    }

    private async Task MakeSuperAdminAsync()
    {
        await using var db = NewDb();
        var role = await db.Roles.FirstOrDefaultAsync(r => r.Code == "SuperAdmin" && r.TargetEntityType == "COMPANY" && r.DeletedAt == null);
        if (role is null)
        {
            role = new Role { Id = Guid.NewGuid(), Code = "SuperAdmin", Name = "Super Administrador", TargetEntityType = "COMPANY", ProductCode = "plataforma", IsSystem = true, IsActive = true, CreatedAt = DateTimeOffset.UtcNow };
            db.Roles.Add(role);
            await db.SaveChangesAsync();
        }

        // Un rol por producto: el usuario queda solo con SuperAdmin, sin ningún rol de Trámites.
        await db.UserRoleAssignments.Where(a => a.UserId == _userId).ExecuteDeleteAsync();
        var assignment = new UserRoleAssignment { Id = Guid.CreateVersion7(), TenantId = _tenantId, UserId = _userId, RoleId = role.Id, AssignedAt = DateTimeOffset.UtcNow, CreatedAt = DateTimeOffset.UtcNow };
        db.UserRoleAssignments.Add(assignment);
        await db.SaveChangesAsync();
    }

    private async Task SeedAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FlitDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var now = DateTimeOffset.UtcNow;
        db.Tenants.Add(new Tenant { Id = _tenantId, Code = $"IT-OIDCP-{_suffix}", LegalName = "Empresa OIDC producto", TaxId = TestNit.Unique(), TenantType = "RENTING", IsActive = true, CreatedAt = now });
        db.Users.Add(new User { Id = _userId, Email = Email, DisplayName = "OIDC producto", Status = "active", HomeTenantId = _tenantId, CreatedAt = now });
        db.UserCredentials.Add(new UserCredential { Id = Guid.CreateVersion7(), UserId = _userId, PasswordHash = hasher.Hash(Password), CreatedAt = now });

        var tramitesModule = new SecurityModule { Id = Guid.NewGuid(), Code = $"oidc{_suffix}", Name = "Módulo OIDC", ProductCode = "tramites", SortOrder = 99, IsActive = true, CreatedAt = now };
        var platformModule = new SecurityModule { Id = Guid.NewGuid(), Code = $"oidcplat{_suffix}", Name = "Módulo OIDC plataforma", ProductCode = "plataforma", SortOrder = 99, IsActive = true, CreatedAt = now };
        db.SecurityModules.AddRange(tramitesModule, platformModule);
        var tramitesAction = new RbacAction { Id = Guid.NewGuid(), ModuleId = tramitesModule.Id, Slug = TramitesSlug, Name = "Leer", HttpMethod = "GET", RoutePattern = "/x", IsActive = true, CreatedAt = now };
        var platformAction = new RbacAction { Id = Guid.NewGuid(), ModuleId = platformModule.Id, Slug = PlatformSlug, Name = "Leer", HttpMethod = "GET", RoutePattern = "/y", IsActive = true, CreatedAt = now };
        db.RbacActions.AddRange(tramitesAction, platformAction);
        _modules.AddRange([tramitesModule.Id, platformModule.Id]);
        _actions.AddRange([tramitesAction.Id, platformAction.Id]);

        db.Roles.Add(new Role { Id = _tramitesRoleId, Code = $"OidcTram-{_suffix}", Name = "Rol Trámites OIDC", TargetEntityType = "COMPANY", ProductCode = "tramites", IsActive = true, CreatedAt = now });
        db.Roles.Add(new Role { Id = _platformRoleId, Code = $"OidcPlat-{_suffix}", Name = "Rol plataforma OIDC", TargetEntityType = "COMPANY", ProductCode = "plataforma", IsActive = true, CreatedAt = now });
        await db.SaveChangesAsync();

        db.RoleGrants.Add(new RoleGrant { Id = Guid.NewGuid(), RoleId = _tramitesRoleId, PermissionId = tramitesAction.Id, CreatedAt = now });
        db.RoleGrants.Add(new RoleGrant { Id = Guid.NewGuid(), RoleId = _platformRoleId, PermissionId = platformAction.Id, CreatedAt = now });
        db.UserRoleAssignments.Add(new UserRoleAssignment { Id = Guid.CreateVersion7(), TenantId = _tenantId, UserId = _userId, RoleId = _tramitesRoleId, AssignedAt = now, CreatedAt = now });
        db.UserRoleAssignments.Add(new UserRoleAssignment { Id = Guid.CreateVersion7(), TenantId = _tenantId, UserId = _userId, RoleId = _platformRoleId, AssignedAt = now, CreatedAt = now });
        await db.SaveChangesAsync();
    }

    public void Dispose()
    {
        using var db = NewDb();
        db.Database.ExecuteSql($"DELETE FROM identity.oidc_tokens WHERE subject = {_userId.ToString()}");
        db.Database.ExecuteSql($"DELETE FROM identity.oidc_authorizations WHERE subject = {_userId.ToString()}");
        db.UserRoleAssignments.Where(a => a.UserId == _userId).ExecuteDelete();
        db.RoleGrants.Where(g => g.RoleId == _tramitesRoleId || g.RoleId == _platformRoleId).ExecuteDelete();
        db.Roles.Where(r => r.Id == _tramitesRoleId || r.Id == _platformRoleId).ExecuteDelete();
        db.RbacActions.Where(a => _actions.Contains(a.Id)).ExecuteDelete();
        db.SecurityModules.Where(m => _modules.Contains(m.Id)).ExecuteDelete();
        db.UserCredentials.Where(c => c.UserId == _userId).ExecuteDelete();
        db.Set<TenantProductEntity>().Where(r => r.TenantId == _tenantId).ExecuteDelete();
        db.TenantConfigAuditLogs.Where(a => a.TenantId == _tenantId).ExecuteDelete();
        db.Users.Where(u => u.Id == _userId).ExecuteDelete();
        db.Tenants.Where(t => t.Id == _tenantId).ExecuteDelete();
    }
}
