using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Identity;
using Flit.Infrastructure.Persistence.Entities.Platform;
using Flit.Infrastructure.Persistence.Entities.Security;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace Flit.Admin.Tests.Platform;

/// <summary>
/// HU #12966 (B-06) — endpoints de plataforma (contrato v1 §6) y <c>RequireProduct</c> (§9) contra la API real
/// (WebApplicationFactory + FlitDbContext real, mismo patrón que <c>SuperAdminRoleGovernanceEndpointsTests</c>).
/// </summary>
public sealed class PlatformEndpointsTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private static readonly SymmetricSecurityKey DummyKey = new(Encoding.UTF8.GetBytes(new string('k', 64)));

    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _superAdminId = Guid.NewGuid();
    private readonly Guid _roleId = Guid.NewGuid();
    private readonly string _suffix = Guid.NewGuid().ToString("N")[..10];

    public PlatformEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
        SeedAsync().GetAwaiter().GetResult();
    }

    // ── GET /me/apps ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task MeApps_UsuarioConRolEnTramites_VeElHubYTramites()
    {
        Use(_client, "Radicador", _userId);

        var apps = await GetAppCodesAsync(_client);

        apps.Should().Equal("plataforma", "tramites");
    }

    [Fact]
    public async Task MeApps_TramitesApagado_SoloVeElHub()
    {
        await SetTramitesAsync(false);
        Use(_client, "Radicador", _userId);

        (await GetAppCodesAsync(_client)).Should().Equal("plataforma");
    }

    [Fact]
    public async Task MeApps_SuperAdmin_VeTodosLosProductos()
    {
        Use(_client, "SuperAdmin", Guid.NewGuid());

        (await GetAppCodesAsync(_client)).Should().Equal("plataforma", "tramites", "comparendos", "diagnostico", "demo");
    }

    // ── /admin/tenants/{tenantId}/products ────────────────────────────────────────

    [Fact]
    public async Task SuperAdmin_EnciendeComparendos_YLoVeEnElListado()
    {
        // Usuario real: la auditoría (admin.tenant_config_audit_logs.changed_by) tiene FK a identity.users.
        Use(_client, "SuperAdmin", _superAdminId);

        var put = await _client.PutAsJsonAsync($"/api/v1/platform/admin/tenants/{_tenantId}/products/comparendos",
            new { enabled = true, notes = "Piloto" }, TestContext.Current.CancellationToken);
        put.StatusCode.Should().Be(HttpStatusCode.OK);

        var list = await _client.GetFromJsonAsync<JsonElement>($"/api/v1/platform/admin/tenants/{_tenantId}/products", TestContext.Current.CancellationToken);
        var comparendos = list.EnumerateArray().Single(p => p.GetProperty("productCode").GetString() == "comparendos");
        comparendos.GetProperty("enabled").GetBoolean().Should().BeTrue();
        comparendos.GetProperty("notes").GetString().Should().Be("Piloto");
    }

    [Theory]
    [InlineData("flotas", HttpStatusCode.NotFound, "PRODUCT_NOT_FOUND")]
    [InlineData("plataforma", HttpStatusCode.BadRequest, "PRODUCT_ALWAYS_ON")]
    public async Task SuperAdmin_ProductoInvalido_ErrorConCodigo(string product, HttpStatusCode status, string code)
    {
        Use(_client, "SuperAdmin", Guid.NewGuid());

        var put = await _client.PutAsJsonAsync($"/api/v1/platform/admin/tenants/{_tenantId}/products/{product}",
            new { enabled = true }, TestContext.Current.CancellationToken);

        put.StatusCode.Should().Be(status);
        (await CodeOfAsync(put)).Should().Be(code);
    }

    [Fact]
    public async Task AdminCompany_NoPuedeEncenderProductos()
    {
        Use(_client, "AdminCompany", _userId, _tenantId);

        var put = await _client.PutAsJsonAsync($"/api/v1/platform/admin/tenants/{_tenantId}/products/comparendos",
            new { enabled = true }, TestContext.Current.CancellationToken);

        put.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ── PUT /products/{code}/manifest ─────────────────────────────────────────────

    [Fact]
    public async Task Manifiesto_SinScope_403()
    {
        Use(_client, "Radicador", _userId);

        var put = await _client.PutAsJsonAsync("/api/v1/platform/products/demo/manifest", Manifest("demo." + _suffix + ".read"), TestContext.Current.CancellationToken);

        put.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Manifiesto_ConScope_CreaUnaVez_YEsIdempotente()
    {
        UseService(_client);

        var first = await _client.PutAsJsonAsync("/api/v1/platform/products/demo/manifest", Manifest("demo." + _suffix + ".read"), TestContext.Current.CancellationToken);
        first.StatusCode.Should().Be(HttpStatusCode.OK);
        var created = await first.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        created.GetProperty("modulesCreated").GetInt32().Should().Be(1);
        created.GetProperty("permissionsCreated").GetInt32().Should().Be(1);
        created.GetProperty("rolesCreated").GetInt32().Should().Be(1);

        var second = await _client.PutAsJsonAsync("/api/v1/platform/products/demo/manifest", Manifest("demo." + _suffix + ".read"), TestContext.Current.CancellationToken);
        var again = await second.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        again.GetProperty("modulesCreated").GetInt32().Should().Be(0);
        again.GetProperty("permissionsCreated").GetInt32().Should().Be(0);
        again.GetProperty("rolesCreated").GetInt32().Should().Be(0);

        await using var db = CreateDbContext();
        (await db.SecurityModules.SingleAsync(m => m.Code == "demo-" + _suffix, TestContext.Current.CancellationToken)).ProductCode.Should().Be("demo");
        (await db.Roles.SingleAsync(r => r.Code == "demo_" + _suffix, TestContext.Current.CancellationToken)).ProductCode.Should().Be("demo");
    }

    [Fact]
    public async Task Manifiesto_SlugDeOtroProducto_400()
    {
        UseService(_client);

        var put = await _client.PutAsJsonAsync("/api/v1/platform/products/demo/manifest", Manifest("tramites." + _suffix + ".read"), TestContext.Current.CancellationToken);

        put.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await CodeOfAsync(put)).Should().Be("MANIFEST_SLUG_PREFIX");
    }

    // ── RequireProduct ────────────────────────────────────────────────────────────

    [Fact]
    public async Task RequireProduct_Encendida_TramitesApagado_403ProductNotEnabled()
    {
        await SetTramitesAsync(false);
        var client = _factory.WithWebHostBuilder(b => b.UseSetting("Suite:ProductAccess:Enforce", "true")).CreateClient();
        Use(client, "Radicador", _userId);

        var response = await client.GetAsync($"/api/v1/tramites/instances/{Guid.NewGuid()}/status-history", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await CodeOfAsync(response)).Should().Be("PRODUCT_NOT_ENABLED");
    }

    [Fact]
    public async Task RequireProduct_Apagada_SoloRegistra_YNoBloquea()
    {
        await SetTramitesAsync(false);
        Use(_client, "Radicador", _userId);

        var response = await _client.GetAsync($"/api/v1/tramites/instances/{Guid.NewGuid()}/status-history", TestContext.Current.CancellationToken);

        (await CodeOfAsync(response)).Should().NotBe("PRODUCT_NOT_ENABLED");
    }

    [Fact]
    public async Task RequireProduct_SuperAdmin_PasaAunqueEsteApagado()
    {
        await SetTramitesAsync(false);
        var client = _factory.WithWebHostBuilder(b => b.UseSetting("Suite:ProductAccess:Enforce", "true")).CreateClient();
        Use(client, "SuperAdmin", Guid.NewGuid());

        var response = await client.GetAsync($"/api/v1/tramites/instances/{Guid.NewGuid()}/status-history", TestContext.Current.CancellationToken);

        (await CodeOfAsync(response)).Should().NotBe("PRODUCT_NOT_ENABLED");
    }

    // ── Ayudas ────────────────────────────────────────────────────────────────────

    private object Manifest(string slug) => new
    {
        version = "1.0.0",
        modules = new[] { new { code = "demo-" + _suffix, name = "Demo IT", permissions = new[] { new { slug, name = "Leer" } } } },
        defaultRoles = new[] { new { code = "demo_" + _suffix, name = "Demo IT", permissions = new[] { slug } } },
    };

    private static async Task<List<string>> GetAppCodesAsync(HttpClient client)
    {
        var apps = await client.GetFromJsonAsync<JsonElement>("/api/v1/platform/me/apps", TestContext.Current.CancellationToken);
        return apps.EnumerateArray().Select(a => a.GetProperty("code").GetString()!).ToList();
    }

    private static async Task<string?> CodeOfAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        if (string.IsNullOrWhiteSpace(body) || !body.TrimStart().StartsWith('{'))
            return null;
        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    private void Use(HttpClient client, string role, Guid userId, Guid? tenantId = null) =>
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Mint(
        [
            new Claim("sub", userId.ToString()),
            new Claim("role", role),
            new Claim("tenant_id", (tenantId ?? _tenantId).ToString()),
        ]));

    private static void UseService(HttpClient client) =>
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Mint(
        [
            new Claim("sub", "svc-demo"),
            new Claim("scope", "platform.manifest"),
        ]));

    private static string Mint(Claim[] claims) => new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
    {
        Issuer = "https://api.flit.co",
        Audience = "flit-api",
        Subject = new ClaimsIdentity(claims),
        Expires = DateTime.UtcNow.AddHours(1),
        SigningCredentials = new SigningCredentials(DummyKey, SecurityAlgorithms.HmacSha256),
    });

    private FlitDbContext CreateDbContext() => _factory.Services.CreateScope().ServiceProvider.GetRequiredService<FlitDbContext>();

    private async Task SetTramitesAsync(bool enabled)
    {
        await using var db = CreateDbContext();
        var row = await db.Set<TenantProductEntity>().SingleAsync(r => r.TenantId == _tenantId && r.ProductCode == "tramites");
        row.Enabled = enabled;
        await db.SaveChangesAsync();
    }

    private async Task SeedAsync()
    {
        await using var db = CreateDbContext();
        db.Tenants.Add(new Tenant
        {
            Id = _tenantId, Code = $"IT-B06-{_suffix}", LegalName = "Empresa B-06 de prueba", TaxId = TestNit.Unique(),
            TenantType = "RENTING", IsActive = true, CreatedAt = DateTimeOffset.UtcNow,
        });
        db.Users.Add(new User
        {
            Id = _userId, Email = $"b06-{_suffix}@flit.local", DisplayName = "Usuario B-06", Status = "active",
            HomeTenantId = _tenantId, CreatedAt = DateTimeOffset.UtcNow,
        });
        db.Users.Add(new User
        {
            Id = _superAdminId, Email = $"b06-sa-{_suffix}@flit.local", DisplayName = "SuperAdmin B-06", Status = "active",
            HomeTenantId = _tenantId, CreatedAt = DateTimeOffset.UtcNow,
        });
        db.Roles.Add(new Role
        {
            Id = _roleId, Code = $"B06Radicador-{_suffix}", Name = "Radicador B-06", TargetEntityType = "COMPANY",
            ProductCode = "tramites", IsActive = true, CreatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();

        db.UserRoleAssignments.Add(new UserRoleAssignment
        {
            Id = Guid.CreateVersion7(), TenantId = _tenantId, UserId = _userId, RoleId = _roleId,
            AssignedAt = DateTimeOffset.UtcNow, CreatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();
    }

    public void Dispose()
    {
        // Borrado en orden de dependencias: los hijos antes que la empresa.
        using var db = CreateDbContext();
        var moduleIds = db.SecurityModules.Where(m => m.Code == "demo-" + _suffix).Select(m => m.Id).ToList();
        var permissionIds = db.RbacActions.Where(p => moduleIds.Contains(p.ModuleId)).Select(p => p.Id).ToList();
        db.RoleGrants.Where(g => permissionIds.Contains(g.PermissionId) || g.RoleId == _roleId).ExecuteDelete();
        db.UserRoleAssignments.Where(a => a.UserId == _userId || a.TenantId == _tenantId).ExecuteDelete();
        db.Roles.Where(r => r.Code == "demo_" + _suffix || r.Id == _roleId).ExecuteDelete();
        db.RbacActions.Where(p => permissionIds.Contains(p.Id)).ExecuteDelete();
        db.SecurityModules.Where(m => moduleIds.Contains(m.Id)).ExecuteDelete();
        db.Set<TenantProductEntity>().Where(r => r.TenantId == _tenantId).ExecuteDelete();
        db.TenantConfigAuditLogs.Where(a => a.TenantId == _tenantId).ExecuteDelete();
        db.Users.Where(u => u.Id == _userId || u.Id == _superAdminId).ExecuteDelete();
        db.Tenants.Where(t => t.Id == _tenantId).ExecuteDelete();
        _client.Dispose();
    }
}
