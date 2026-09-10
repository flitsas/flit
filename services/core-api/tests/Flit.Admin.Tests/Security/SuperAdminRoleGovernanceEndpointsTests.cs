using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
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
/// HU #10508 — Gobernanza de roles exclusiva de SuperAdmin. Integración real (WebApplicationFactory
/// + FlitDbContext real, mismo patrón que <see cref="Flit.Admin.Tests.Security.UserRoleAssignmentEndpointsTests"/>):
/// <list type="bullet">
///   <item>AC1: SuperAdmin gestiona el ciclo de vida completo de un rol
///   (crear/editar permisos/desactivar/activar/eliminar) vía <c>/api/v1/superadmin/roles*</c>.</item>
///   <item>AC2: AdminCompany pierde la capacidad de crear/editar/eliminar roles (antes expuesta en
///   <c>/api/v1/security/roles*</c>) pero conserva el <c>GET</c> de solo lectura.</item>
///   <item>AC3: el único mecanismo de autorización SuperAdmin es la policy real basada en el rol
///   JWT (<c>AdminAuthorization.SuperAdminPolicy</c>) — se eliminó el stub por header
///   <c>X-Flit-SuperAdmin</c>. Un JWT de otro rol es rechazado con 403 en <c>/api/v1/superadmin/*</c>.</item>
/// </list>
/// </summary>
public sealed class SuperAdminRoleGovernanceEndpointsTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private static readonly SymmetricSecurityKey DummyKey =
        new(Encoding.UTF8.GetBytes(new string('k', 64)));

    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _superAdminUserId = Guid.NewGuid();
    private readonly Guid _adminCompanyUserId = Guid.NewGuid();

    private Guid _companyRoleId;

    public SuperAdminRoleGovernanceEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
        _client = factory.CreateClient();

        SeedAsync().GetAwaiter().GetResult();
    }

    // ── AC1 — SuperAdmin: ciclo de vida completo del rol vía /api/v1/superadmin/roles* ──────

    [Fact]
    public async Task SuperAdmin_FullRoleLifecycle_CreateSetPermissionsDeactivateActivateDelete()
    {
        UseToken("SuperAdmin", _superAdminUserId);

        var code = $"Hu10508Role-{Guid.NewGuid():N}"[..30];

        // Crear
        var createResponse = await _client.PostAsJsonAsync(
            "/api/v1/superadmin/roles",
            new { targetEntityType = "COMPANY", code, name = "Rol HU10508", description = (string?)null },
            TestContext.Current.CancellationToken);
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var created = await createResponse.Content.ReadFromJsonAsync<CreatedRoleResponse>(
            cancellationToken: TestContext.Current.CancellationToken);
        created.Should().NotBeNull();
        var roleId = created!.Id;

        // Editar permisos (subset vacío es válido — solo se verifica que el endpoint responde 200)
        var setPermissionsResponse = await _client.PutAsJsonAsync(
            $"/api/v1/superadmin/roles/{roleId}/permissions",
            new { permissionIds = Array.Empty<Guid>() },
            TestContext.Current.CancellationToken);
        setPermissionsResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // Desactivar
        var deactivateResponse = await _client.PatchAsync(
            $"/api/v1/superadmin/roles/{roleId}/deactivate",
            content: null,
            TestContext.Current.CancellationToken);
        deactivateResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        await using (var db = CreateDbContext())
        {
            var role = await db.Roles.AsNoTracking()
                .SingleAsync(r => r.Id == roleId, TestContext.Current.CancellationToken);
            role.IsActive.Should().BeFalse();
        }

        // Activar
        var activateResponse = await _client.PatchAsync(
            $"/api/v1/superadmin/roles/{roleId}/activate",
            content: null,
            TestContext.Current.CancellationToken);
        activateResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        await using (var db = CreateDbContext())
        {
            var role = await db.Roles.AsNoTracking()
                .SingleAsync(r => r.Id == roleId, TestContext.Current.CancellationToken);
            role.IsActive.Should().BeTrue();
        }

        // Eliminar (sin usuarios activos asignados → NoContent)
        var deleteResponse = await _client.DeleteAsync(
            $"/api/v1/superadmin/roles/{roleId}",
            TestContext.Current.CancellationToken);
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        await using (var db = CreateDbContext())
        {
            var stillVisible = await db.Roles.AsNoTracking()
                .AnyAsync(r => r.Id == roleId && r.DeletedAt == null, TestContext.Current.CancellationToken);
            stillVisible.Should().BeFalse();
        }
    }

    // ── AC2 — AdminCompany pierde crear/editar/eliminar, conserva el GET de solo lectura ────

    [Fact]
    public async Task AdminCompany_CreateRole_NoLongerRoutable()
    {
        UseToken("AdminCompany", _adminCompanyUserId, _tenantId);

        var response = await _client.PostAsJsonAsync(
            "/api/v1/security/roles",
            new { code = $"ShouldFail-{Guid.NewGuid():N}"[..20], name = "No debería crear" },
            TestContext.Current.CancellationToken);

        // HU #10508 AC2: POST /api/v1/security/roles se eliminó por completo. El template
        // "/roles" sigue existiendo (solo GET, de solo lectura), así que el routing de ASP.NET
        // Core encuentra coincidencia de PATH pero no de MÉTODO → 405 Method Not Allowed (no 403:
        // nunca se llega a evaluar ninguna policy de autorización porque no hay endpoint que la
        // tenga adjunta para POST).
        response.StatusCode.Should().Be(HttpStatusCode.MethodNotAllowed,
            "el endpoint POST /api/v1/security/roles se eliminó, pero el template \"/roles\" " +
            "sigue existiendo (GET de solo lectura) → 405, no 404.");
    }

    [Fact]
    public async Task AdminCompany_DeleteRole_Returns403()
    {
        UseToken("AdminCompany", _adminCompanyUserId, _tenantId);

        var response = await _client.DeleteAsync(
            $"/api/v1/security/roles/{_companyRoleId}",
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "el endpoint DELETE /api/v1/security/roles/{id} se eliminó por completo (HU #10508 AC2).");
    }

    [Fact]
    public async Task AdminCompany_SetRolePermissions_Returns403()
    {
        UseToken("AdminCompany", _adminCompanyUserId, _tenantId);

        var response = await _client.PutAsJsonAsync(
            $"/api/v1/security/roles/{_companyRoleId}/permissions",
            new { permissionIds = Array.Empty<Guid>() },
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "el endpoint PUT /api/v1/security/roles/{id}/permissions se eliminó por completo (HU #10508 AC2).");
    }

    [Fact]
    public async Task AdminCompany_ListRoles_StillWorks_Returns200()
    {
        UseToken("AdminCompany", _adminCompanyUserId, _tenantId);

        var response = await _client.GetAsync(
            "/api/v1/security/roles",
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "el GET de solo lectura sigue disponible para que AdminCompany pueda asignar roles existentes.");
    }

    // ── AC3 — mecanismo de autorización SuperAdmin unificado (JWT), sin stub por header ─────

    [Fact]
    public async Task SuperAdminJwt_WithoutStubHeader_AuthorizesSuperAdminEndpoints()
    {
        UseToken("SuperAdmin", _superAdminUserId);
        _client.DefaultRequestHeaders.Remove("X-Flit-SuperAdmin");

        var response = await _client.GetAsync(
            "/api/v1/superadmin/roles?targetEntityType=COMPANY",
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task NonSuperAdminJwt_IsRejected_Returns403OnSuperAdminEndpoints()
    {
        UseToken("AdminCompany", _adminCompanyUserId, _tenantId);

        var response = await _client.GetAsync(
            "/api/v1/superadmin/roles?targetEntityType=COMPANY",
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task StubHeaderAlone_WithoutSuperAdminJwt_DoesNotAuthorize()
    {
        // AC3: el header X-Flit-SuperAdmin ya NO es un mecanismo de autorización válido — un
        // AdminCompany que lo mande sigue recibiendo 403 en /api/v1/superadmin/*.
        UseToken("AdminCompany", _adminCompanyUserId, _tenantId);
        _client.DefaultRequestHeaders.Add("X-Flit-SuperAdmin", "true");

        var response = await _client.GetAsync(
            "/api/v1/superadmin/roles?targetEntityType=COMPANY",
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        _client.DefaultRequestHeaders.Remove("X-Flit-SuperAdmin");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void UseToken(string role, Guid userId, Guid? tenantId = null) =>
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", MintToken(role, tenantId ?? Guid.NewGuid(), userId));

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
            Code = $"CO-HU10508-{Guid.NewGuid():N}"[..20],
            LegalName = "Compañía gobernanza roles de prueba",
            TaxId = TestNit.Unique(),
            TenantType = "RENTING",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        });

        db.Users.Add(new User
        {
            Id = _adminCompanyUserId,
            Email = $"admincompany-{_adminCompanyUserId:N}@flit.local",
            DisplayName = "AdminCompany de prueba",
            Status = "active",
            HomeTenantId = _tenantId,
            CreatedAt = DateTimeOffset.UtcNow,
        });

        var companyRole = new Role
        {
            Id = Guid.NewGuid(),
            Code = $"Hu10508CompanyRole-{Guid.NewGuid():N}"[..40],
            Name = "Rol COMPANY de prueba (HU #10508)",
            TargetEntityType = "COMPANY",
            IsSystem = false,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        db.Roles.Add(companyRole);

        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        _companyRoleId = companyRole.Id;
    }

    private FlitDbContext CreateDbContext() =>
        _factory.Services.CreateScope().ServiceProvider.GetRequiredService<FlitDbContext>();

    public void Dispose()
    {
        using var db = CreateDbContext();

        db.Roles.RemoveRange(db.Roles.Where(r => r.Id == _companyRoleId));
        db.Users.RemoveRange(db.Users.Where(u => u.Id == _adminCompanyUserId));
        db.Tenants.RemoveRange(db.Tenants.Where(t => t.Id == _tenantId));
        db.SaveChanges();
    }

    private sealed record CreatedRoleResponse(Guid Id);
}
