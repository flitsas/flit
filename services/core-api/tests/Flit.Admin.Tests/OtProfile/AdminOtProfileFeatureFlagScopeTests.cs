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

namespace Flit.Admin.Tests.OtProfile;

/// <summary>
/// HU #12854 (Feature #12847, Épica #12751) — <c>PATCH /profile</c> y
/// <c>PATCH /feature-flags/{id}</c> resuelven el tenant objetivo con
/// <c>ResolveOtUserScopeAsync</c>, el MISMO helper que ya usa Requisitos
/// (HU #10546 / <see cref="OtRequirements.AdminOtRequirementsScopeTests"/>). Antes de esta HU un
/// Super Admin que llamaba estos dos endpoints escribía SIEMPRE en su propio tenant "fantasma",
/// ignorando el organismo que estaba viendo en pantalla.
/// </summary>
public sealed class AdminOtProfileFeatureFlagScopeTests
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

    public AdminOtProfileFeatureFlagScopeTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
        SeedAsync().GetAwaiter().GetResult();
    }

    [Fact] // HU12854_AC1 — PATCH /profile?transitOfficeId=X escribe en el tenant OT dueño de X.
    public async Task HU12854_AC1_PatchProfile_AsSuperAdmin_WritesRequestedOfficeTenant()
    {
        Authenticate("SuperAdmin", _superAdminTenantId, _superAdminUserId);

        var response = await _client.PatchAsJsonAsync(
            $"/api/v1/admin/ot/profile?transitOfficeId={_officeId}",
            new { operationMode = "quipux" },
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        await using var db = CreateDbContext();
        var profile = await db.TransitOfficeProfiles.AsNoTracking()
            .SingleAsync(p => p.TenantId == _tenantId, TestContext.Current.CancellationToken);
        profile.OperationMode.Should().Be("quipux", "el PATCH debe escribir en el tenant del organismo pedido");

        var superAdminHasProfile = await db.TransitOfficeProfiles.AsNoTracking()
            .AnyAsync(p => p.TenantId == _superAdminTenantId, TestContext.Current.CancellationToken);
        superAdminHasProfile.Should().BeFalse("el tenant del SuperAdmin no debe recibir el perfil OT");
    }

    [Fact] // HU12854_AC2 — SuperAdmin sin ?transitOfficeId recibe 400 y no escribe en ningún tenant.
    public async Task HU12854_AC2_PatchProfile_AsSuperAdmin_WithoutTransitOfficeId_Returns400()
    {
        Authenticate("SuperAdmin", _superAdminTenantId, _superAdminUserId);

        var response = await _client.PatchAsJsonAsync(
            "/api/v1/admin/ot/profile",
            new { operationMode = "quipux" },
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        await using var db = CreateDbContext();
        var superAdminHasProfile = await db.TransitOfficeProfiles.AsNoTracking()
            .AnyAsync(p => p.TenantId == _superAdminTenantId, TestContext.Current.CancellationToken);
        superAdminHasProfile.Should().BeFalse("sin transitOfficeId el SuperAdmin no debe escribir en ningún tenant");
    }

    // HU12854_AC3 (ot_admin sigue usando su propio tenant sin exigir ?transitOfficeId) se prueba
    // directamente sobre ResolveOtUserScopeAsync en AdminOtUserScopeResolutionTests: HU #12855
    // (mismo Feature #12847) restringe PATCH /profile a SuperAdmin, así que un ot_admin real
    // recibe 403 por autorización antes de llegar al scoping (ver
    // HU12855_AC1_PatchProfile_AsNonSuperAdmin_Returns403 en AdminOtFeatureBAuthorizationTests).

    [Fact] // HU12854_AC1 — PATCH /feature-flags/{id}?transitOfficeId=X activa el flag del organismo pedido.
    public async Task HU12854_AC1_PatchFeatureFlag_AsSuperAdmin_WritesRequestedOfficeTenant()
    {
        var flagId = await SeedFeatureFlagAsync();

        Authenticate("SuperAdmin", _superAdminTenantId, _superAdminUserId);

        var response = await _client.PatchAsJsonAsync(
            $"/api/v1/admin/ot/feature-flags/{flagId}?transitOfficeId={_officeId}",
            new { isEnabled = true },
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        await using var db = CreateDbContext();
        var entity = await db.OtFeatureFlags.AsNoTracking()
            .SingleAsync(f => f.Id == flagId, TestContext.Current.CancellationToken);
        entity.IsEnabled.Should().BeTrue();
        entity.TenantId.Should().Be(_tenantId, "el flag debe pertenecer al tenant OT dueño, no al del SuperAdmin");
    }

    [Fact] // HU12854_AC2 — SuperAdmin sin ?transitOfficeId recibe 400 (no busca el flag en su propio tenant).
    public async Task HU12854_AC2_PatchFeatureFlag_AsSuperAdmin_WithoutTransitOfficeId_Returns400()
    {
        var flagId = await SeedFeatureFlagAsync();

        Authenticate("SuperAdmin", _superAdminTenantId, _superAdminUserId);

        var response = await _client.PatchAsJsonAsync(
            $"/api/v1/admin/ot/feature-flags/{flagId}",
            new { isEnabled = true },
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // HU12854_AC3 (ot_admin sigue usando su propio tenant sin exigir ?transitOfficeId) se prueba
    // directamente sobre ResolveOtUserScopeAsync en AdminOtUserScopeResolutionTests: HU #12855
    // restringe PATCH /feature-flags/{id} a SuperAdmin (ver
    // HU12855_AC1_PatchFeatureFlag_AsNonSuperAdmin_Returns403 en AdminOtFeatureBAuthorizationTests).

    private async Task<Guid> SeedFeatureFlagAsync()
    {
        var flagId = Guid.NewGuid();
        await using var seed = CreateDbContext();
        seed.OtFeatureFlags.Add(new OtFeatureFlagEntity
        {
            Id = flagId,
            TenantId = _tenantId,
            FlagKey = $"flag-scope-{flagId:N}",
            IsEnabled = false,
            Config = "{}",
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        return flagId;
    }

    private void Authenticate(string role, Guid tenantId, Guid userId) =>
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", MintToken(role, tenantId, userId));

    private async Task SeedAsync()
    {
        await using var db = CreateDbContext();

        db.TransitOffices.Add(NewOffice(_officeId, "OT profile/flags scope"));
        db.Tenants.AddRange(
            NewTenant(_tenantId, "OT Profile Scope"),
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
        Code = $"OT-PRO-{Guid.NewGuid():N}"[..20],
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

    public void Dispose()
    {
        using var db = CreateDbContext();

        db.OtFeatureFlags.RemoveRange(db.OtFeatureFlags.Where(f =>
            f.TenantId == _tenantId || f.TenantId == _superAdminTenantId));
        db.TransitOfficeProfiles.RemoveRange(db.TransitOfficeProfiles.Where(p =>
            p.TenantId == _tenantId || p.TenantId == _superAdminTenantId));
        db.SaveChanges();

        db.Users.RemoveRange(db.Users.Where(u => u.Id == _superAdminUserId));
        db.Tenants.RemoveRange(db.Tenants.Where(t => t.Id == _tenantId || t.Id == _superAdminTenantId));
        db.TransitOffices.RemoveRange(db.TransitOffices.Where(o => o.Id == _officeId));
        db.SaveChanges();
    }
}
