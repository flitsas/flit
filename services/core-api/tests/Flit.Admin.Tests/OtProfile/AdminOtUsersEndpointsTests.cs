using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Admin;
using Flit.Infrastructure.Persistence.Entities.Catalogs;
using Flit.Infrastructure.Persistence.Entities.Identity;
using Flit.Infrastructure.Persistence.Entities.Security;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace Flit.Admin.Tests.OtProfile;

/// <summary>
/// Self-service de usuarios OT (refactor adminOT) — <c>POST /api/v1/admin/ot/users/invite</c>,
/// <c>GET /api/v1/admin/ot/users</c>, <c>POST</c>/<c>DELETE /api/v1/admin/ot/users/{userId}/suspend</c>.
/// Integración real contra la BD de desarrollo (WebApplicationFactory + FlitDbContext real —
/// mismo patrón que <see cref="AdminOtAuthorizationTests"/>): seeda su propio tenant OT,
/// oficina de catálogo, rol <c>ot_admin</c> y usuarios, con GUIDs aleatorios por ejecución
/// para no chocar con datos existentes, y limpia lo creado al finalizar.
/// </summary>
public sealed class AdminOtUsersEndpointsTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private static readonly SymmetricSecurityKey DummyKey =
        new(Encoding.UTF8.GetBytes(new string('k', 64)));

    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    private readonly Guid _transitOfficeId = Guid.NewGuid();
    private readonly Guid _otTenantId = Guid.NewGuid();

    // HU #10505: security.roles es un catálogo GLOBAL — "ot_admin" ya no se crea por tenant en
    // el seed de este test; se resuelve por Code contra la fila global sembrada por
    // DevelopmentAuthSeeder al levantar la WebApplicationFactory.
    private Guid _roleId;

    private readonly Guid _superAdminUserId = Guid.NewGuid();
    private readonly Guid _otAdminUserId = Guid.NewGuid();
    private readonly Guid _collaboratorUserId = Guid.NewGuid();

    public AdminOtUsersEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
        _client = factory.CreateClient();

        SeedAsync().GetAwaiter().GetResult();
    }

    [Fact]
    public async Task Invite_AsSuperAdmin_TargetingTransitOfficeId_ResolvesOtAdminRole_Returns201()
    {
        var token = MintToken("SuperAdmin", Guid.NewGuid(), _superAdminUserId);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var email = $"superadmin-invite-{Guid.NewGuid():N}@flit.local";
        var response = await _client.PostAsJsonAsync(
            $"/api/v1/admin/ot/users/invite?transitOfficeId={_transitOfficeId}",
            new { email, fullName = "Primer AdminOT" },
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        await using var db = CreateDbContext();
        var invitation = await db.UserInvitations.AsNoTracking()
            .SingleAsync(i => i.Email == email, TestContext.Current.CancellationToken);
        invitation.TenantId.Should().Be(_otTenantId);
        invitation.RoleId.Should().Be(_roleId);
    }

    [Fact]
    public async Task Invite_AsOtAdmin_WithoutQueryParam_UsesOwnTenant_Returns201()
    {
        var token = MintToken("ot_admin", _otTenantId, _otAdminUserId);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var email = $"ot-admin-invite-{Guid.NewGuid():N}@flit.local";
        var response = await _client.PostAsJsonAsync(
            "/api/v1/admin/ot/users/invite",
            new { email, fullName = "Colaborador OT" },
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        await using var db = CreateDbContext();
        var invitation = await db.UserInvitations.AsNoTracking()
            .SingleAsync(i => i.Email == email, TestContext.Current.CancellationToken);
        invitation.TenantId.Should().Be(_otTenantId);
        invitation.RoleId.Should().Be(_roleId);
    }

    [Fact]
    public async Task Invite_AsSuperAdmin_WithoutTransitOfficeId_Returns400()
    {
        var token = MintToken("SuperAdmin", Guid.NewGuid(), _superAdminUserId);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.PostAsJsonAsync(
            "/api/v1/admin/ot/users/invite",
            new { email = "sin-scope@flit.local" },
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ListUsers_AsOtAdmin_ReturnsCollaboratorInOwnTenant()
    {
        var token = MintToken("ot_admin", _otTenantId, _otAdminUserId);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.GetAsync("/api/v1/admin/ot/users", TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<ListUsersBody>(
            cancellationToken: TestContext.Current.CancellationToken);
        body.Should().NotBeNull();
        body!.Data.Should().Contain(u => u.Id == _collaboratorUserId.ToString());
    }

    [Fact]
    public async Task Suspend_ThenUnsuspend_AsOtAdmin_TogglesActiveSuspension()
    {
        var token = MintToken("ot_admin", _otTenantId, _otAdminUserId);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var suspendResponse = await _client.PostAsJsonAsync(
            $"/api/v1/admin/ot/users/{_collaboratorUserId}/suspend",
            new { reason = "Prueba automatizada", endsAt = DateTimeOffset.UtcNow.AddDays(1) },
            TestContext.Current.CancellationToken);
        suspendResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        await using (var db = CreateDbContext())
        {
            var now = DateTimeOffset.UtcNow;
            var hasActiveSuspension = await db.UserTempSuspensions.AsNoTracking().AnyAsync(
                s => s.UserId == _collaboratorUserId && s.TenantId == _otTenantId
                     && s.DeletedAt == null && s.StartsAt <= now && s.EndsAt >= now,
                TestContext.Current.CancellationToken);
            hasActiveSuspension.Should().BeTrue();
        }

        var unsuspendResponse = await _client.DeleteAsync(
            $"/api/v1/admin/ot/users/{_collaboratorUserId}/suspend", TestContext.Current.CancellationToken);
        unsuspendResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        await using (var db = CreateDbContext())
        {
            var now = DateTimeOffset.UtcNow;
            var hasActiveSuspension = await db.UserTempSuspensions.AsNoTracking().AnyAsync(
                s => s.UserId == _collaboratorUserId && s.TenantId == _otTenantId
                     && s.DeletedAt == null && s.StartsAt <= now && s.EndsAt >= now,
                TestContext.Current.CancellationToken);
            hasActiveSuspension.Should().BeFalse();
        }
    }

    [Fact]
    public async Task Suspend_AsOtAdmin_TargetingUserOutsideTenant_Returns404()
    {
        var token = MintToken("ot_admin", _otTenantId, _otAdminUserId);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.PostAsJsonAsync(
            $"/api/v1/admin/ot/users/{Guid.NewGuid()}/suspend",
            new { reason = "Usuario ajeno", endsAt = DateTimeOffset.UtcNow.AddDays(1) },
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // HU #10619 AC1 — sin endsAt: desactivación indefinida (sin fecha de fin), hasta reactivación manual.
    [Fact]
    public async Task Suspend_WithoutEndsAt_CreatesIndefiniteSuspension()
    {
        var token = MintToken("ot_admin", _otTenantId, _otAdminUserId);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.PostAsJsonAsync(
            $"/api/v1/admin/ot/users/{_collaboratorUserId}/suspend",
            new { reason = "Desactivación indefinida de prueba" },
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        await using var db = CreateDbContext();
        var suspension = await db.UserTempSuspensions.AsNoTracking()
            .Where(s => s.UserId == _collaboratorUserId && s.TenantId == _otTenantId && s.DeletedAt == null)
            .OrderByDescending(s => s.CreatedAt)
            .FirstAsync(TestContext.Current.CancellationToken);

        suspension.EndsAt.Should().BeNull();
    }

    // HU #10619 AC4 — suspender/desactivar al único ot_admin activo del tenant se rechaza (409),
    // para no dejar el tenant sin ningún administrador disponible.
    [Fact]
    public async Task Suspend_TargetIsLastActiveOtAdmin_Returns409()
    {
        var soloTenantId = Guid.NewGuid();
        var soloAdminUserId = Guid.NewGuid();
        var soloTransitOfficeId = Guid.NewGuid();

        await using (var db = CreateDbContext())
        {
            db.TransitOffices.Add(new TransitOffice
            {
                Id = soloTransitOfficeId,
                Code = $"T{Guid.NewGuid():N}"[..10],
                Name = "OT solo-admin tests",
                DepartmentCode = "99",
                CityCode = "99999",
                IsActive = true,
            });
            db.Tenants.Add(new Tenant
            {
                Id = soloTenantId,
                Code = $"OT-SOLO-{Guid.NewGuid():N}"[..20],
                LegalName = "OT Solo Admin Tests",
                TaxId = "900888888-8",
                TenantType = "RENTING",
                IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow,
            });
            db.Users.Add(new User
            {
                Id = soloAdminUserId,
                Email = $"solo-admin-{soloAdminUserId:N}@flit.local",
                DisplayName = "Solo AdminOT",
                Status = "active",
                HomeTenantId = soloTenantId,
                CreatedAt = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);

            db.TransitOfficeProfiles.Add(new TransitOfficeProfile
            {
                Id = Guid.NewGuid(),
                TenantId = soloTenantId,
                TransitOfficeId = soloTransitOfficeId,
                OperationMode = "dashboard",
                CreatedAt = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);

            db.UserRoleAssignments.Add(new UserRoleAssignment
            {
                Id = Guid.NewGuid(),
                TenantId = soloTenantId,
                UserId = soloAdminUserId,
                RoleId = _roleId,
                AssignedAt = DateTimeOffset.UtcNow,
                CreatedAt = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        try
        {
            // SuperAdmin (no es el propio objetivo) intenta suspender al único ot_admin del tenant.
            var token = MintToken("SuperAdmin", Guid.NewGuid(), _superAdminUserId);
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var response = await _client.PostAsJsonAsync(
                $"/api/v1/admin/ot/users/{soloAdminUserId}/suspend?transitOfficeId={soloTransitOfficeId}",
                new { reason = "Intento de dejar el tenant sin administradores" },
                TestContext.Current.CancellationToken);

            response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        }
        finally
        {
            // Mismo orden de dependencia FK que Dispose(): hijos primero (su propio SaveChanges),
            // padres después.
            await using var db = CreateDbContext();
            db.UserRoleAssignments.RemoveRange(db.UserRoleAssignments.Where(a => a.TenantId == soloTenantId));
            db.TransitOfficeProfiles.RemoveRange(db.TransitOfficeProfiles.Where(p => p.TenantId == soloTenantId));
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);

            db.Users.RemoveRange(db.Users.Where(u => u.Id == soloAdminUserId));
            db.Tenants.RemoveRange(db.Tenants.Where(t => t.Id == soloTenantId));
            db.TransitOffices.RemoveRange(db.TransitOffices.Where(o => o.Id == soloTransitOfficeId));
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }
    }

    private async Task SeedAsync()
    {
        await using var db = CreateDbContext();

        db.TransitOffices.Add(new TransitOffice
        {
            Id = _transitOfficeId,
            Code = $"T{Guid.NewGuid():N}"[..10],
            Name = "OT self-service tests",
            DepartmentCode = "99",
            CityCode = "99999",
            IsActive = true,
        });

        db.Tenants.Add(new Tenant
        {
            Id = _otTenantId,
            Code = $"OT-TEST-{Guid.NewGuid():N}"[..20],
            LegalName = "OT Self-Service Tests",
            TaxId = "900999999-9",
            TenantType = "RENTING",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        });

        db.Users.Add(new User
        {
            Id = _superAdminUserId,
            Email = $"superadmin-{_superAdminUserId:N}@flit.local",
            DisplayName = "SuperAdmin de prueba",
            Status = "active",
            CreatedAt = DateTimeOffset.UtcNow,
        });

        // Tenant + Users primero (sin FK entre sí): el perfil OT depende del tenant recién
        // insertado, y no hay navegación EF entre estos agregados que le permita a
        // SaveChanges inferir el orden de inserción por sí solo.
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        // HU #10505 / ADR-0023: security.roles es un catálogo GLOBAL (sin tenant_id) — se
        // reutiliza la fila "ot_admin" si ya existe (p.ej. sembrada por DevelopmentAuthSeeder)
        // o se crea aquí mismo si no (BD limpia, como la que usa CI): el test no puede depender
        // de que el seeder de desarrollo haya corrido antes, solo de que exista una única fila
        // global (violaría UNIQUE(code, target_entity_type) crear una por tenant de prueba).
        var existingOtAdminRole = await db.Roles.AsNoTracking()
            .Where(r => r.Code == "ot_admin" && r.TargetEntityType == "TRANSIT_OFFICE" && r.DeletedAt == null)
            .Select(r => r.Id)
            .SingleOrDefaultAsync(TestContext.Current.CancellationToken);

        if (existingOtAdminRole == Guid.Empty)
        {
            var newRole = new Role
            {
                Id = Guid.NewGuid(),
                Code = "ot_admin",
                Name = "Administrador OT",
                TargetEntityType = "TRANSIT_OFFICE",
                IsSystem = true,
                IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow,
            };
            db.Roles.Add(newRole);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
            existingOtAdminRole = newRole.Id;
        }

        _roleId = existingOtAdminRole;

        db.TransitOfficeProfiles.Add(new TransitOfficeProfile
        {
            Id = Guid.NewGuid(),
            TenantId = _otTenantId,
            TransitOfficeId = _transitOfficeId,
            OperationMode = "dashboard",
            CreatedAt = DateTimeOffset.UtcNow,
        });

        db.Users.Add(new User
        {
            Id = _otAdminUserId,
            Email = $"otadmin-{_otAdminUserId:N}@flit.local",
            DisplayName = "AdminOT de prueba",
            Status = "active",
            HomeTenantId = _otTenantId,
            CreatedAt = DateTimeOffset.UtcNow,
        });

        db.Users.Add(new User
        {
            Id = _collaboratorUserId,
            Email = $"colaborador-{_collaboratorUserId:N}@flit.local",
            DisplayName = "Colaborador de prueba",
            Status = "active",
            HomeTenantId = _otTenantId,
            CreatedAt = DateTimeOffset.UtcNow,
        });

        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        // HU #10619 AC4 — la guarda de "último administrador activo" exige que exista un rol
        // ot_admin ACTIVO persistido (no solo el claim del JWT) para que suspender al
        // colaborador no lo deje como único ot_admin del tenant.
        db.UserRoleAssignments.Add(new UserRoleAssignment
        {
            Id = Guid.NewGuid(),
            TenantId = _otTenantId,
            UserId = _otAdminUserId,
            RoleId = _roleId,
            AssignedAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow,
        });

        db.UserRoleAssignments.Add(new UserRoleAssignment
        {
            Id = Guid.NewGuid(),
            TenantId = _otTenantId,
            UserId = _collaboratorUserId,
            RoleId = _roleId,
            AssignedAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow,
        });

        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

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

    /// <summary>
    /// Limpieza en orden de dependencia FK (RESTRICT en varias tablas de <c>security</c>
    /// hacia <c>identity.tenants</c>/<c>identity.users</c>/<c>security.roles</c>): cada
    /// etapa hace su propio <c>SaveChanges</c> porque, sin navegaciones EF entre estos
    /// agregados, el batching no puede inferir el orden correcto de borrado.
    /// </summary>
    public void Dispose()
    {
        using var db = CreateDbContext();

        db.UserRoleAssignments.RemoveRange(db.UserRoleAssignments.Where(a => a.TenantId == _otTenantId));
        db.UserTempSuspensions.RemoveRange(db.UserTempSuspensions.Where(s => s.TenantId == _otTenantId));
        db.UserInvitations.RemoveRange(db.UserInvitations.Where(i => i.TenantId == _otTenantId));
        db.TransitOfficeProfiles.RemoveRange(db.TransitOfficeProfiles.Where(p => p.TenantId == _otTenantId));
        db.SaveChanges();

        // HU #10505: "ot_admin" es un rol del catálogo GLOBAL, sembrado una sola vez por
        // DevelopmentAuthSeeder — este test NO lo crea ni lo borra, solo lo resuelve.

        db.Users.RemoveRange(db.Users.Where(u =>
            u.Id == _superAdminUserId || u.Id == _otAdminUserId || u.Id == _collaboratorUserId));
        db.Tenants.RemoveRange(db.Tenants.Where(t => t.Id == _otTenantId));
        db.TransitOffices.RemoveRange(db.TransitOffices.Where(o => o.Id == _transitOfficeId));
        db.SaveChanges();
    }

    private sealed record ListUsersBody(List<ListUserItem> Data);

    private sealed record ListUserItem(string Id, string FullName, string Email, string Status);
}
