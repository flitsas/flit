using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json.Serialization;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Identity;
using Flit.Infrastructure.Persistence.Entities.Security;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace Flit.Admin.Tests.Security;

/// <summary>
/// Fix post-review #10504 — hallazgos de seguridad/correctness sobre el refactor de
/// Roles/Permisos SuperAdmin+multi-rol (HU #10505/#10506/#10508), cubiertos contra una BD
/// real (mismo patrón que <see cref="MultiRoleUserAssignmentEndpointsTests"/>):
/// <list type="bullet">
///   <item>Fix 1 — <c>GET /api/v1/security/roles</c> ya no expone roles inactivos ni el rol de
///   sistema SuperAdmin al checklist de invitación de AdminCompany/OtAdmin.</item>
///   <item>Fix 3 — <c>PUT /users/{userId}/role</c> y <c>DELETE /users/{userId}/roles/{roleId}</c>
///   ahora exigen explícitamente la policy AdminCompany (antes solo exigían autenticación).</item>
/// </list>
/// </summary>
public sealed class SecurityEndpointsHardeningTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private static readonly SymmetricSecurityKey DummyKey =
        new(Encoding.UTF8.GetBytes(new string('k', 64)));

    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _adminUserId = Guid.NewGuid();
    private readonly Guid _radicadorUserId = Guid.NewGuid();
    private readonly Guid _targetUserId = Guid.NewGuid();

    private Guid _activeRoleId;
    private Guid _inactiveRoleId;
    private Guid _superAdminRoleId;

    public SecurityEndpointsHardeningTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
        _client = factory.CreateClient();

        SeedAsync().GetAwaiter().GetResult();
    }

    // ── Fix 1 — GET /security/roles filtra inactivos y SuperAdmin ───────────────

    [Fact]
    public async Task ListRoles_ExcludesInactiveRoles_AndSuperAdminRole()
    {
        UseToken(_adminUserId, "AdminCompany");

        var response = await _client.GetAsync("/api/v1/security/roles", TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var roles = await response.Content.ReadFromJsonAsync<List<RoleSummaryDto>>(
            cancellationToken: TestContext.Current.CancellationToken);
        roles.Should().NotBeNull();

        var ids = roles!.Select(r => r.Id).ToList();

        ids.Should().Contain(_activeRoleId, "un rol activo, no-SuperAdmin, debe seguir visible");
        ids.Should().NotContain(_inactiveRoleId, "un rol inactivo no debe ofrecerse para invitar usuarios");
        ids.Should().NotContain(_superAdminRoleId, "el rol de sistema SuperAdmin nunca debe ser seleccionable por un tenant");
        roles!.Should().OnlyContain(r => r.IsActive, "todos los roles devueltos deben estar activos");
        roles!.Should().OnlyContain(
            r => !string.Equals(r.Code, "SuperAdmin", StringComparison.OrdinalIgnoreCase),
            "ningún rol devuelto debe ser el rol de sistema SuperAdmin");
    }

    // ── Fix 3 — PUT /users/{userId}/role y DELETE .../roles/{roleId} exigen AdminCompany ──

    [Fact]
    public async Task AssignRole_CallerWithoutAdminCompanyPolicy_Returns403()
    {
        UseToken(_radicadorUserId, "Radicador");

        var response = await _client.PutAsJsonAsync(
            $"/api/v1/security/users/{_targetUserId}/role",
            new { roleId = _activeRoleId },
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "un usuario sin rol AdminCompany/SuperAdmin no debe poder asignar roles a otros usuarios");
    }

    [Fact]
    public async Task RemoveRoleAssignment_CallerWithoutAdminCompanyPolicy_Returns403()
    {
        UseToken(_radicadorUserId, "Radicador");

        var response = await _client.DeleteAsync(
            $"/api/v1/security/users/{_targetUserId}/roles/{_activeRoleId}",
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "un usuario sin rol AdminCompany/SuperAdmin no debe poder quitar roles a otros usuarios");
    }

    [Fact]
    public async Task AssignRole_CallerWithAdminCompanyPolicy_IsAuthorized()
    {
        UseToken(_adminUserId, "AdminCompany");

        var response = await _client.PutAsJsonAsync(
            $"/api/v1/security/users/{_targetUserId}/role",
            new { roleId = _activeRoleId },
            TestContext.Current.CancellationToken);

        // No debe ser 403/401: la policy se satisface. El resultado exacto (200/409) depende del
        // estado previo del usuario objetivo, que no es el foco de este test de autorización.
        response.StatusCode.Should().NotBe(HttpStatusCode.Forbidden);
        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void UseToken(Guid userId, string role) =>
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", MintToken(role, _tenantId, userId));

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

    private async Task SeedAsync()
    {
        await using var db = CreateDbContext();

        db.Tenants.Add(new Tenant
        {
            Id = _tenantId,
            Code = $"CO-HU10504-{Guid.NewGuid():N}"[..20],
            LegalName = "Compañía hallazgos post-review de prueba",
            TaxId = "900777777-7",
            TenantType = "RENTING",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        });

        db.Users.Add(new User
        {
            Id = _adminUserId,
            Email = $"admin-{_adminUserId:N}@flit.local",
            DisplayName = "AdminCompany de prueba",
            Status = "active",
            HomeTenantId = _tenantId,
            CreatedAt = DateTimeOffset.UtcNow,
        });

        db.Users.Add(new User
        {
            Id = _radicadorUserId,
            Email = $"radicador-{_radicadorUserId:N}@flit.local",
            DisplayName = "Radicador de prueba",
            Status = "active",
            HomeTenantId = _tenantId,
            CreatedAt = DateTimeOffset.UtcNow,
        });

        db.Users.Add(new User
        {
            Id = _targetUserId,
            Email = $"target-{_targetUserId:N}@flit.local",
            DisplayName = "Usuario objetivo de prueba",
            Status = "active",
            HomeTenantId = _tenantId,
            CreatedAt = DateTimeOffset.UtcNow,
        });

        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var activeRole = new Role
        {
            Id = Guid.NewGuid(),
            Code = $"Hu10504ActiveRole-{Guid.NewGuid():N}"[..40],
            Name = "Rol activo de prueba (HU #10504)",
            TargetEntityType = "COMPANY",
            IsSystem = false,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        var inactiveRole = new Role
        {
            Id = Guid.NewGuid(),
            Code = $"Hu10504InactiveRole-{Guid.NewGuid():N}"[..40],
            Name = "Rol inactivo de prueba (HU #10504)",
            TargetEntityType = "COMPANY",
            IsSystem = false,
            IsActive = false,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        // security.roles es un catálogo GLOBAL — reutiliza el rol SuperAdmin si otro test ya lo
        // sembró en la misma corrida (Code, TargetEntityType) es la unicidad real del catálogo.
        var superAdminRole = await db.Roles.AsNoTracking()
            .FirstOrDefaultAsync(r => r.Code == "SuperAdmin" && r.TargetEntityType == "COMPANY",
                TestContext.Current.CancellationToken);

        if (superAdminRole is null)
        {
            superAdminRole = new Role
            {
                Id = Guid.NewGuid(),
                Code = "SuperAdmin",
                Name = "Super Administrador",
                TargetEntityType = "COMPANY",
                IsSystem = true,
                IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow,
            };
            db.Roles.Add(superAdminRole);
        }

        db.Roles.AddRange(activeRole, inactiveRole);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        _activeRoleId = activeRole.Id;
        _inactiveRoleId = inactiveRole.Id;
        _superAdminRoleId = superAdminRole.Id;
    }

    private FlitDbContext CreateDbContext() =>
        _factory.Services.CreateScope().ServiceProvider.GetRequiredService<FlitDbContext>();

    public void Dispose()
    {
        using var db = CreateDbContext();

        db.UserRoleAssignments.RemoveRange(db.UserRoleAssignments.Where(a => a.TenantId == _tenantId));
        db.SaveChanges();

        db.Users.RemoveRange(db.Users.Where(u =>
            u.Id == _adminUserId || u.Id == _radicadorUserId || u.Id == _targetUserId));
        db.Roles.RemoveRange(db.Roles.Where(r => r.Id == _activeRoleId || r.Id == _inactiveRoleId));
        db.Tenants.RemoveRange(db.Tenants.Where(t => t.Id == _tenantId));
        db.SaveChanges();
    }

    private sealed record RoleSummaryDto(
        Guid Id,
        [property: JsonPropertyName("targetEntityType")] string TargetEntityType,
        string Code,
        string Name,
        string? Description,
        [property: JsonPropertyName("isSystem")] bool IsSystem,
        [property: JsonPropertyName("isActive")] bool IsActive,
        [property: JsonPropertyName("permissionCount")] int PermissionCount,
        DateTimeOffset CreatedAt);
}
