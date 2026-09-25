using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using Flit.Api.Authorization;
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

namespace Flit.Admin.Tests.Authorization;

/// <summary>
/// HU #12855 (Feature #12847, Épica #12751) — Reglas (POST/GET/PATCH /rules), Requisitos
/// (GET/PUT /requirements), <c>PATCH /profile</c> y <c>PATCH /feature-flags/{id}</c> exigen
/// <see cref="AdminAuthorization.SuperAdminPolicy"/> por encima de la policy general del módulo
/// OT (<see cref="AdminAuthorization.OtModulePolicy"/>): ni <c>ot_admin</c> ni ningún otro rol de
/// un tenant OT (p. ej. <c>gestor_tramites_ot</c>) pueden leer ni escribir estas configuraciones.
/// <c>GET /profile</c> (sin PATCH) NO cambia: sigue abierto a cualquier usuario de un tenant OT —
/// lo usan <c>Shell.goOtHub</c>/<c>OtHubLayout</c> para resolver el organismo antes de saber el rol.
/// </summary>
public sealed class AdminOtFeatureBAuthorizationTests
    : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private static readonly SymmetricSecurityKey DummyKey =
        new(Encoding.UTF8.GetBytes(new string('k', 64)));

    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    private readonly Guid _officeId = Guid.NewGuid();
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _otUserId = Guid.NewGuid();

    private readonly Guid _superAdminTenantId = Guid.NewGuid();
    private readonly Guid _superAdminUserId = Guid.NewGuid();

    public AdminOtFeatureBAuthorizationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
        SeedAsync().GetAwaiter().GetResult();
    }

    public static TheoryData<string> NonSuperAdminRoles => new() { "ot_admin", "gestor_tramites_ot" };

    // ── AC1 — ot_admin y gestor_tramites_ot reciben 403 en Reglas ──────────────────────────────

    [Theory]
    [MemberData(nameof(NonSuperAdminRoles))]
    public async Task HU12855_AC1_CreateRule_AsNonSuperAdmin_Returns403(string role)
    {
        AuthenticateOtUser(role);

        var response = await _client.PostAsJsonAsync(
            "/api/v1/admin/ot/rules",
            NewRulePayload("Intento no autorizado"),
            TestContext.Current.CancellationToken);

        await AssertOtModuleForbiddenAsync(response);
    }

    [Theory]
    [MemberData(nameof(NonSuperAdminRoles))]
    public async Task HU12855_AC1_ListRules_AsNonSuperAdmin_Returns403(string role)
    {
        AuthenticateOtUser(role);

        var response = await _client.GetAsync(
            "/api/v1/admin/ot/rules", TestContext.Current.CancellationToken);

        await AssertOtModuleForbiddenAsync(response);
    }

    [Theory]
    [MemberData(nameof(NonSuperAdminRoles))]
    public async Task HU12855_AC1_UpdateRule_AsNonSuperAdmin_Returns403(string role)
    {
        AuthenticateOtUser(role);

        var response = await _client.PatchAsJsonAsync(
            $"/api/v1/admin/ot/rules/{Guid.NewGuid()}",
            new { isEnabled = false },
            TestContext.Current.CancellationToken);

        await AssertOtModuleForbiddenAsync(response);
    }

    // ── AC1 — ot_admin y gestor_tramites_ot reciben 403 en Requisitos ──────────────────────────

    [Theory]
    [MemberData(nameof(NonSuperAdminRoles))]
    public async Task HU12855_AC1_GetRequirements_AsNonSuperAdmin_Returns403(string role)
    {
        AuthenticateOtUser(role);

        var response = await _client.GetAsync(
            "/api/v1/admin/ot/requirements", TestContext.Current.CancellationToken);

        await AssertOtModuleForbiddenAsync(response);
    }

    [Theory]
    [MemberData(nameof(NonSuperAdminRoles))]
    public async Task HU12855_AC1_PutRequirements_AsNonSuperAdmin_Returns403(string role)
    {
        AuthenticateOtUser(role);

        var response = await _client.PutAsJsonAsync(
            "/api/v1/admin/ot/requirements",
            new { requiresRnmc = true },
            TestContext.Current.CancellationToken);

        await AssertOtModuleForbiddenAsync(response);
    }

    // ── AC1 — ot_admin y gestor_tramites_ot reciben 403 en PATCH /profile ──────────────────────

    [Theory]
    [MemberData(nameof(NonSuperAdminRoles))]
    public async Task HU12855_AC1_PatchProfile_AsNonSuperAdmin_Returns403(string role)
    {
        AuthenticateOtUser(role);

        var response = await _client.PatchAsJsonAsync(
            "/api/v1/admin/ot/profile",
            new { operationMode = "quipux" },
            TestContext.Current.CancellationToken);

        await AssertOtModuleForbiddenAsync(response);
    }

    // ── AC1 — ot_admin y gestor_tramites_ot reciben 403 en PATCH /feature-flags/{id} ───────────

    [Theory]
    [MemberData(nameof(NonSuperAdminRoles))]
    public async Task HU12855_AC1_PatchFeatureFlag_AsNonSuperAdmin_Returns403(string role)
    {
        AuthenticateOtUser(role);

        var response = await _client.PatchAsJsonAsync(
            $"/api/v1/admin/ot/feature-flags/{Guid.NewGuid()}",
            new { isEnabled = true },
            TestContext.Current.CancellationToken);

        await AssertOtModuleForbiddenAsync(response);
    }

    // ── AC2 — Super Admin con ?transitOfficeId válido sigue operando (200/201) ─────────────────

    [Fact]
    public async Task HU12855_AC2_CreateRule_AsSuperAdmin_Returns201()
    {
        AuthenticateSuperAdmin();

        var response = await _client.PostAsJsonAsync(
            $"/api/v1/admin/ot/rules?transitOfficeId={_officeId}",
            NewRulePayload("HU12855 AC2"),
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task HU12855_AC2_GetRequirements_AsSuperAdmin_Returns200()
    {
        AuthenticateSuperAdmin();

        var response = await _client.GetAsync(
            $"/api/v1/admin/ot/requirements?transitOfficeId={_officeId}",
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task HU12855_AC2_PatchProfile_AsSuperAdmin_Returns200()
    {
        AuthenticateSuperAdmin();

        var response = await _client.PatchAsJsonAsync(
            $"/api/v1/admin/ot/profile?transitOfficeId={_officeId}",
            new { operationMode = "quipux" },
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ── AC3 — GET /profile (sin PATCH) sigue abierto a cualquier usuario de un tenant OT ───────

    [Theory]
    [InlineData("SuperAdmin")]
    [InlineData("ot_admin")]
    [InlineData("gestor_tramites_ot")]
    public async Task HU12855_AC3_GetProfile_AnyOtUser_Returns200(string role)
    {
        if (role == "SuperAdmin")
        {
            AuthenticateSuperAdmin();
        }
        else
        {
            AuthenticateOtUser(role);
        }

        // Sin ?transitOfficeId=, GetProfileAsync usa el tenant del JWT (AdminOtEndpoints
        // .GetProfileAsync): ot_admin/gestor_tramites_ot leen el perfil sembrado en SeedAsync
        // para _tenantId; SuperAdmin lee el de _superAdminTenantId, que no tiene fila y
        // GetOtProfileHandler devuelve un perfil por defecto SIN persistir (nunca null/404).
        // Por eso 200 es alcanzable para los tres roles sin sembrar nada adicional.
        var response = await _client.GetAsync(
            "/api/v1/admin/ot/profile", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK, "GET /profile no exige SuperAdmin");
    }

    /// <summary>
    /// Asegura 403 con el mensaje de <see cref="AdminAuthorization.OtModuleForbiddenMessage"/>, NO
    /// <see cref="AdminAuthorization.ForbiddenMessage"/> ("se requiere rol SuperAdmin"). Es el
    /// comportamiento real y deliberado: estos endpoints combinan la <c>OtModulePolicy</c> del grupo
    /// (<c>MapGroup("/api/v1/admin/ot").RequireAuthorization(OtModulePolicy)</c>) con la
    /// <c>SuperAdminPolicy</c> propia del endpoint — igual que <c>suspend</c>/<c>unsuspend</c> de
    /// usuarios, que ya reforzaban SuperAdminPolicy sobre el grupo antes de esta HU. ASP.NET Core
    /// combina ambas policies en una sola (todos sus requirements), y
    /// <see cref="SuperAdminForbiddenResultHandler.ResolveForbiddenMessage"/> prioriza
    /// <c>OtModuleRequirement</c> sobre el requirement de rol al elegir el mensaje.
    /// </summary>
    private static async Task AssertOtModuleForbiddenAsync(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var body = await response.Content.ReadFromJsonAsync<ForbiddenBody>(
            TestContext.Current.CancellationToken);

        body.Should().NotBeNull();
        body!.Error.Should().Be(AdminAuthorization.OtModuleForbiddenMessage);
    }

    private sealed record ForbiddenBody(string Error);

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

    /// <summary>Rol de un tenant OT (ot_admin o cualquier otro, p. ej. gestor_tramites_ot con
    /// entity_type=TRANSIT_OFFICE) — pasa la policy general del módulo pero no SuperAdminPolicy.</summary>
    private void AuthenticateOtUser(string role) =>
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", MintToken(role, _tenantId, _otUserId, entityType: AdminAuthorization.TransitOfficeEntityType));

    private void AuthenticateSuperAdmin() =>
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", MintToken("SuperAdmin", _superAdminTenantId, _superAdminUserId));

    private async Task SeedAsync()
    {
        await using var db = CreateDbContext();

        db.TransitOffices.Add(NewOffice(_officeId, "OT authz Feature B"));
        db.Tenants.AddRange(
            NewTenant(_tenantId, "OT Feature B Authz"),
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
        Code = $"OT-AUTHZ-{Guid.NewGuid():N}"[..20],
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

    private static string MintToken(string role, Guid tenantId, Guid userId, string? entityType = null)
    {
        var handler = new JsonWebTokenHandler();
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

        return handler.CreateToken(new SecurityTokenDescriptor
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
