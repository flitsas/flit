using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Admin;
using Flit.Infrastructure.Persistence.Entities.Catalogs;
using Flit.Infrastructure.Persistence.Entities.Identity;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace Flit.Admin.Tests.OtRules;

/// <summary>
/// HU #12854 (Feature #12847, Épica #12751) — Reglas OT (POST/GET/PATCH /rules) resuelve el tenant
/// objetivo con el MISMO helper (<c>ResolveOtUserScopeAsync</c>) que ya usa Requisitos
/// (HU #10546 / <see cref="Flit.Admin.Tests.OtRequirements.AdminOtRequirementsScopeTests"/>).
/// Antes de esta HU el SuperAdmin creaba/leía/actualizaba reglas SIEMPRE en su propio tenant
/// "fantasma" (el del JWT), ignorando <c>?transitOfficeId</c>: el organismo que veía en pantalla
/// nunca recibía la regla configurada.
/// </summary>
public sealed class AdminOtRulesScopeTests
    : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private static readonly SymmetricSecurityKey DummyKey =
        new(Encoding.UTF8.GetBytes(new string('k', 64)));

    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    private readonly Guid _officeId = Guid.NewGuid();
    private readonly Guid _tenantId = Guid.NewGuid();

    // El SuperAdmin vive en un tenant propio SIN perfil de OT — como el de producción/QA.
    private readonly Guid _superAdminTenantId = Guid.NewGuid();
    private readonly Guid _superAdminUserId = Guid.NewGuid();

    public AdminOtRulesScopeTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
        SeedAsync().GetAwaiter().GetResult();
    }

    [Fact] // HU12854_AC1 — POST /rules?transitOfficeId=X crea la regla en el tenant OT dueño de X.
    public async Task HU12854_AC1_CreateRule_AsSuperAdmin_PersistsUnderRequestedOfficeTenant()
    {
        Authenticate("SuperAdmin", _superAdminTenantId, _superAdminUserId);

        var response = await _client.PostAsJsonAsync(
            $"/api/v1/admin/ot/rules?transitOfficeId={_officeId}",
            NewRulePayload("Bloqueo HU12854"),
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        await using var db = CreateDbContext();
        var entity = await db.OtFeatureFlags.AsNoTracking()
            .SingleAsync(f => f.TenantId == _tenantId, TestContext.Current.CancellationToken);
        entity.TenantId.Should().Be(_tenantId, "la regla debe pertenecer al tenant OT dueño, no al del SuperAdmin");

        var superAdminHasRule = await db.OtFeatureFlags.AsNoTracking()
            .AnyAsync(f => f.TenantId == _superAdminTenantId, TestContext.Current.CancellationToken);
        superAdminHasRule.Should().BeFalse("el tenant del SuperAdmin no debe recibir la regla");
    }

    [Fact] // HU12854_AC1 — GET /rules?transitOfficeId=X lista las reglas del organismo pedido.
    public async Task HU12854_AC1_ListRules_AsSuperAdmin_ReturnsRequestedOfficeRules()
    {
        var ruleId = await SeedRuleAsync();

        Authenticate("SuperAdmin", _superAdminTenantId, _superAdminUserId);

        var response = await _client.GetAsync(
            $"/api/v1/admin/ot/rules?transitOfficeId={_officeId}",
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<RulesListDto>(
            cancellationToken: TestContext.Current.CancellationToken);
        body!.Data.Should().ContainSingle(r => r.Id == ruleId);
    }

    [Fact] // HU12854_AC1 — PATCH /rules/{id}?transitOfficeId=X actualiza la regla del organismo pedido.
    public async Task HU12854_AC1_UpdateRule_AsSuperAdmin_TogglesRequestedOfficeRule()
    {
        var ruleId = await SeedRuleAsync();

        Authenticate("SuperAdmin", _superAdminTenantId, _superAdminUserId);

        var response = await _client.PatchAsJsonAsync(
            $"/api/v1/admin/ot/rules/{ruleId}?transitOfficeId={_officeId}",
            new { isEnabled = false },
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        await using var db = CreateDbContext();
        var entity = await db.OtFeatureFlags.AsNoTracking()
            .SingleAsync(f => f.Id == ruleId, TestContext.Current.CancellationToken);
        entity.IsEnabled.Should().BeFalse();
        entity.TenantId.Should().Be(_tenantId, "el update debe seguir apuntando al tenant del organismo pedido");
    }

    [Fact] // HU12854_AC2 — SuperAdmin sin ?transitOfficeId recibe 400 y no escribe en su propio tenant.
    public async Task HU12854_AC2_CreateRule_AsSuperAdmin_WithoutTransitOfficeId_Returns400()
    {
        Authenticate("SuperAdmin", _superAdminTenantId, _superAdminUserId);

        var response = await _client.PostAsJsonAsync(
            "/api/v1/admin/ot/rules",
            NewRulePayload("Sin transitOfficeId"),
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        await using var db = CreateDbContext();
        var superAdminHasRule = await db.OtFeatureFlags.AsNoTracking()
            .AnyAsync(f => f.TenantId == _superAdminTenantId, TestContext.Current.CancellationToken);
        superAdminHasRule.Should().BeFalse("sin transitOfficeId, el SuperAdmin no debe escribir en ningún tenant");
    }

    // HU12854_AC3 (ot_admin sigue usando su propio tenant sin exigir ?transitOfficeId) se prueba
    // directamente sobre ResolveOtUserScopeAsync en
    // Flit.Admin.Tests.OtProfile.AdminOtUserScopeResolutionTests: HU #12855 (mismo Feature #12847)
    // restringe este mismo endpoint a SuperAdmin, así que un ot_admin real recibe 403 por
    // autorización antes de llegar al scoping (ver HU12855_AC1_CreateRule_AsNonSuperAdmin_Returns403
    // en AdminOtFeatureBAuthorizationTests).

    private async Task<Guid> SeedRuleAsync()
    {
        var ruleId = Guid.NewGuid();
        await using var seed = CreateDbContext();
        seed.OtFeatureFlags.Add(new OtFeatureFlagEntity
        {
            Id = ruleId,
            TenantId = _tenantId,
            FlagKey = $"rule:{ruleId}",
            IsEnabled = true,
            Config = """{"name":"Preexistente","conditions":[{"field":"x","op":"eq","value":true}],"logic":"AND","action":{"type":"bloquear"}}""",
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        return ruleId;
    }

    private static object NewRulePayload(string name) => new
    {
        name,
        conditions = new[]
        {
            new { field = "deuda_pendiente", op = "eq", value = true },
        },
        logic = "AND",
        action = new { type = "bloquear" },
    };

    private void Authenticate(string role, Guid tenantId, Guid userId) =>
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", MintToken(role, tenantId, userId));

    private async Task SeedAsync()
    {
        await using var db = CreateDbContext();

        db.TransitOffices.Add(NewOffice(_officeId, "OT rules scope"));
        db.Tenants.AddRange(
            NewTenant(_tenantId, "OT Rules Scope"),
            NewTenant(_superAdminTenantId, "Empresa del SuperAdmin (sin perfil OT)"));

        db.Users.Add(new User
        {
            Id = _superAdminUserId,
            Email = $"superadmin-{_superAdminUserId:N}@flit.local",
            DisplayName = "SuperAdmin de prueba",
            Status = "active",
            HomeTenantId = _superAdminTenantId,
            CreatedAt = DateTimeOffset.UtcNow,
        });

        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        db.TransitOfficeProfiles.Add(NewProfile(_tenantId, _officeId));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
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

    private static Tenant NewTenant(Guid id, string legalName) => new()
    {
        Id = id,
        Code = $"OT-RUL-{Guid.NewGuid():N}"[..20],
        LegalName = legalName,
        TaxId = TestNit.Unique(),
        TenantType = "RENTING",
        IsActive = true,
        CreatedAt = DateTimeOffset.UtcNow,
    };

    private static TransitOfficeProfile NewProfile(Guid tenantId, Guid officeId) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = tenantId,
        TransitOfficeId = officeId,
        OperationMode = "dashboard",
        QuipuxReadOnly = false,
        CreatedAt = DateTimeOffset.UtcNow,
    };

    private FlitDbContext CreateDbContext() =>
        _factory.Services.CreateScope().ServiceProvider.GetRequiredService<FlitDbContext>();

    private static string MintToken(string role, Guid tenantId, Guid userId)
    {
        var handler = new JsonWebTokenHandler();
        return handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = "https://api.flit.co",
            Audience = "flit-api",
            Subject = new ClaimsIdentity(
            [
                new Claim("sub", userId.ToString()),
                new Claim("role", role),
                new Claim("tenant_id", tenantId.ToString()),
            ]),
            Expires = DateTime.UtcNow.AddHours(1),
            SigningCredentials = new SigningCredentials(DummyKey, SecurityAlgorithms.HmacSha256),
        });
    }

    private sealed record RuleDto(Guid Id);

    private sealed record RulesListDto(IReadOnlyList<RuleDto> Data);

    public void Dispose()
    {
        using var db = CreateDbContext();

        db.OtFeatureFlags.RemoveRange(db.OtFeatureFlags.Where(f =>
            f.TenantId == _tenantId || f.TenantId == _superAdminTenantId));
        db.TransitOfficeProfiles.RemoveRange(db.TransitOfficeProfiles.Where(p => p.TenantId == _tenantId));
        db.SaveChanges();

        db.Users.RemoveRange(db.Users.Where(u => u.Id == _superAdminUserId));
        db.Tenants.RemoveRange(db.Tenants.Where(t => t.Id == _tenantId || t.Id == _superAdminTenantId));
        db.TransitOffices.RemoveRange(db.TransitOffices.Where(o => o.Id == _officeId));
        db.SaveChanges();
    }
}
