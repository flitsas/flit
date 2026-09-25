using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Flit.Api.Authorization;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Admin;
using Flit.Infrastructure.Persistence.Entities.Catalogs;
using Flit.Infrastructure.Persistence.Entities.Identity;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace Flit.Admin.Tests.Authorization;

/// <summary>
/// Bug #12912 (hallazgos del review del PR #442).
/// <list type="bullet">
///   <item><b>Ley 1581</b> — en la configuración de mandatos del LADO OT (<c>/admin/ot/offices/{id}/mandatos/company-rules</c>
///   y <c>/admin/transit-offices/{id}/mandate-signers/companies</c>) un usuario no SuperAdmin ve las compañías
///   con grant directo y las de la red que ya le entregaron trámites; SuperAdmin ve toda la red. Escribir
///   sobre una compañía que el organismo no puede ver devuelve 404.</item>
///   <item><b>IDOR</b> — el grupo <c>/admin/transit-offices/{transitOfficeId}/mandate-signers</c> exige que
///   el OT pedido sea el del perfil del usuario; SuperAdmin libre. El subgrupo <c>/{id}/identity</c> no
///   lleva la guarda: sus cuatro rutas responden 410 sin tocar datos y su contrato lo fija
///   <c>AdminIdentityDeprecatedEndpointsTests</c>.</item>
/// </list>
/// Semilla: organismo A (perfil del tenant OT del usuario) y organismo B ajeno; Concesión H con grant a A
/// y su hija K sin trámites.
/// <para>Uso de ejemplo: <c>GET /api/v1/admin/ot/offices/{A}/mandatos/company-rules</c> como ot_admin de A
/// devuelve H pero no K.</para>
/// </summary>
public sealed class AdminOtMandatesNetworkScopeTests
    : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private static readonly SymmetricSecurityKey DummyKey =
        new(Encoding.UTF8.GetBytes(new string('k', 64)));

    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    private readonly Guid _officeA = Guid.NewGuid();
    private readonly Guid _officeB = Guid.NewGuid();
    private readonly Guid _otTenantA = Guid.NewGuid();
    private readonly Guid _otUserA = Guid.NewGuid();
    private readonly Guid _superAdminTenantId = Guid.NewGuid();
    private readonly Guid _superAdminUserId = Guid.NewGuid();
    private readonly Guid _head = Guid.NewGuid();
    private readonly Guid _child = Guid.NewGuid();

    public AdminOtMandatesNetworkScopeTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
        SeedAsync().GetAwaiter().GetResult();
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // ── Ley 1581: company-rules del hub OT ──────────────────────────────────────────────────────

    [Fact]
    public async Task CompanyRules_como_ot_admin_no_lista_la_red_sin_tramites()
    {
        AuthenticateOtUser();

        var ids = await ReadCompanyIdsAsync(
            await _client.GetAsync($"/api/v1/admin/ot/offices/{_officeA}/mandatos/company-rules", Ct), "items");

        ids.Should().Contain(_head, "la cabeza tiene grant directo al organismo");
        ids.Should().NotContain(_child, "la hija entra solo por la red y no ha entregado trámites (Ley 1581)");
    }

    [Fact]
    public async Task CompanyRules_como_SuperAdmin_lista_toda_la_red()
    {
        AuthenticateSuperAdmin();

        var ids = await ReadCompanyIdsAsync(
            await _client.GetAsync($"/api/v1/admin/ot/offices/{_officeA}/mandatos/company-rules", Ct), "items");

        ids.Should().Contain([_head, _child]);
    }

    [Fact]
    public async Task CompanyRules_default_signer_de_compania_no_visible_es_404_para_ot_admin()
    {
        AuthenticateOtUser();

        var response = await _client.PatchAsJsonAsync(
            $"/api/v1/admin/ot/offices/{_officeA}/mandatos/company-rules/{_child}/default-signer",
            new { defaultMandateSignerId = (Guid?)null },
            Ct);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task CompanyRules_delete_de_compania_no_visible_es_404_para_ot_admin()
    {
        AuthenticateOtUser();

        var response = await _client.DeleteAsync(
            $"/api/v1/admin/ot/offices/{_officeA}/mandatos/company-rules/{_child}", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── Ley 1581: consola de mandatarios del OT ─────────────────────────────────────────────────

    [Fact]
    public async Task MandateSigners_companies_como_ot_admin_no_lista_la_red_sin_tramites()
    {
        AuthenticateOtUser();

        var ids = await ReadCompanyIdsAsync(
            await _client.GetAsync($"/api/v1/admin/transit-offices/{_officeA}/mandate-signers/companies", Ct),
            "data");

        ids.Should().Contain(_head);
        ids.Should().NotContain(_child);
    }

    [Fact]
    public async Task MandateSigners_companies_como_SuperAdmin_lista_toda_la_red()
    {
        AuthenticateSuperAdmin();

        var ids = await ReadCompanyIdsAsync(
            await _client.GetAsync($"/api/v1/admin/transit-offices/{_officeA}/mandate-signers/companies", Ct),
            "data");

        ids.Should().Contain([_head, _child]);
    }

    // ── IDOR: alcance de OT en mandate-signers ──────────────────────────────────────────────────

    public static TheoryData<string, string> RutasDeOtroOrganismo => new()
    {
        { "GET", "" },
        { "GET", "/companies" },
        { "POST", "" },
        { "PUT", "/11111111-1111-4111-8111-111111111111" },
        { "POST", "/11111111-1111-4111-8111-111111111111/inactivate" },
        { "POST", "/11111111-1111-4111-8111-111111111111/reactivate" },
        { "GET", "/11111111-1111-4111-8111-111111111111/signature-image" },
    };

    [Theory]
    [MemberData(nameof(RutasDeOtroOrganismo))]
    public async Task MandateSigners_de_otro_organismo_es_403_para_ot_admin(string method, string suffix)
    {
        AuthenticateOtUser();

        var response = await SendAsync(method, $"/api/v1/admin/transit-offices/{_officeB}/mandate-signers{suffix}");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        body.GetProperty("code").GetString().Should().Be("TRANSIT_OFFICE_FORBIDDEN");
    }

    [Fact]
    public async Task MandateSigners_del_propio_organismo_es_200_para_ot_admin()
    {
        AuthenticateOtUser();

        var response = await _client.GetAsync($"/api/v1/admin/transit-offices/{_officeA}/mandate-signers", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task MandateSigners_de_cualquier_organismo_es_200_para_SuperAdmin()
    {
        AuthenticateSuperAdmin();

        var response = await _client.GetAsync($"/api/v1/admin/transit-offices/{_officeB}/mandate-signers", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ── Infraestructura ─────────────────────────────────────────────────────────────────────────

    private Task<HttpResponseMessage> SendAsync(string method, string url) => method switch
    {
        "GET" => _client.GetAsync(url, Ct),
        "POST" => _client.PostAsJsonAsync(url, new { }, Ct),
        "PUT" => _client.PutAsJsonAsync(url, new { }, Ct),
        _ => throw new ArgumentOutOfRangeException(nameof(method)),
    };

    private static async Task<IReadOnlyList<Guid>> ReadCompanyIdsAsync(HttpResponseMessage response, string listProperty)
    {
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        return
        [
            .. body.GetProperty(listProperty).EnumerateArray()
                .Select(e => e.GetProperty("companyTenantId").GetGuid()),
        ];
    }

    private void AuthenticateOtUser() =>
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", MintToken("ot_admin", _otTenantA, _otUserA, AdminAuthorization.TransitOfficeEntityType));

    private void AuthenticateSuperAdmin() =>
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", MintToken("SuperAdmin", _superAdminTenantId, _superAdminUserId));

    private async Task SeedAsync()
    {
        await using var db = CreateDbContext();

        db.TransitOffices.AddRange(NewOffice(_officeA, "OT A bug 12912"), NewOffice(_officeB, "OT B bug 12912"));
        db.Tenants.AddRange(
            NewTenant(_otTenantA, "OT A tenant bug 12912", "RENTING", isGroupParent: false, parent: null),
            NewTenant(_superAdminTenantId, "Empresa del SuperAdmin", "RENTING", isGroupParent: false, parent: null),
            NewTenant(_head, "Concesion H bug 12912", "CONCESION", isGroupParent: true, parent: null));
        db.Users.Add(new User
        {
            Id = _superAdminUserId,
            Email = $"superadmin-{_superAdminUserId:N}@flit.local",
            DisplayName = "SuperAdmin de prueba",
            Status = "active",
            HomeTenantId = _superAdminTenantId,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync(Ct);

        // Los triggers de identity.tenants exigen que la cabeza exista antes que la hija.
        db.Tenants.Add(NewTenant(_child, "Hija K bug 12912", "CONCESIONARIO", isGroupParent: false, parent: _head));
        db.TransitOfficeProfiles.Add(new TransitOfficeProfile
        {
            Id = Guid.NewGuid(),
            TenantId = _otTenantA,
            TransitOfficeId = _officeA,
            OperationMode = "dashboard",
            QuipuxReadOnly = false,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        db.TenantTransitOfficeGrants.Add(new TenantTransitOfficeGrant
        {
            Id = Guid.NewGuid(),
            TenantId = _head,
            TransitOfficeId = _officeA,
            IsEnabled = true,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync(Ct);
    }

    private static TransitOffice NewOffice(Guid id, string name) => new()
    {
        Id = id,
        Code = $"R{Guid.NewGuid():N}"[..10],
        Name = name,
        DepartmentCode = "99",
        CityCode = "99999",
        IsActive = true,
    };

    private static Tenant NewTenant(Guid id, string legalName, string type, bool isGroupParent, Guid? parent) => new()
    {
        Id = id,
        Code = $"B12912-{Guid.NewGuid():N}"[..20],
        LegalName = legalName,
        TaxId = TestNit.Unique(),
        TenantType = type,
        IsGroupParent = isGroupParent,
        ParentTenantId = parent,
        IsActive = true,
        CreatedAt = DateTimeOffset.UtcNow,
    };

    private FlitDbContext CreateDbContext() =>
        _factory.Services.CreateScope().ServiceProvider.GetRequiredService<FlitDbContext>();

    private static string MintToken(string role, Guid tenantId, Guid userId, string? entityType = null)
    {
        var claims = new List<Claim>
        {
            new("sub", userId.ToString()),
            new("role", role),
            new("tenant_id", tenantId.ToString()),
        };
        if (entityType is not null)
        {
            claims.Add(new Claim(AdminAuthorization.EntityTypeClaimType, entityType));
        }

        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = "https://api.flit.co",
            Audience = "flit-api",
            Subject = new ClaimsIdentity(claims),
            Expires = DateTime.UtcNow.AddHours(1),
            SigningCredentials = new SigningCredentials(DummyKey, SecurityAlgorithms.HmacSha256),
        });
    }

    public void Dispose()
    {
        using var db = CreateDbContext();

        db.CompanyOtMandateRules.RemoveRange(db.CompanyOtMandateRules.Where(r =>
            r.TransitOfficeId == _officeA || r.TransitOfficeId == _officeB));
        db.TenantTransitOfficeGrants.RemoveRange(db.TenantTransitOfficeGrants.Where(g =>
            g.TenantId == _head || g.TenantId == _child));
        db.TransitOfficeProfiles.RemoveRange(db.TransitOfficeProfiles.Where(p => p.TenantId == _otTenantA));
        db.Users.RemoveRange(db.Users.Where(u => u.Id == _superAdminUserId));
        db.SaveChanges();

        db.Tenants.RemoveRange(db.Tenants.Where(t => t.Id == _child));
        db.SaveChanges();

        db.Tenants.RemoveRange(db.Tenants.Where(t =>
            t.Id == _otTenantA || t.Id == _superAdminTenantId || t.Id == _head));
        db.TransitOffices.RemoveRange(db.TransitOffices.Where(o => o.Id == _officeA || o.Id == _officeB));
        db.SaveChanges();
    }
}
