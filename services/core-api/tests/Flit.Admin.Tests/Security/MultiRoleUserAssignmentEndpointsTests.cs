using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Identity;
using Flit.Infrastructure.Persistence.Entities.Security;
using Flit.Modules.Security.Application.Auth.ActivateAccount;
using Flit.Modules.Security.Domain.Auth;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace Flit.Admin.Tests.Security;

/// <summary>
/// HU #10506 — soporte multi-rol por usuario (modelo aditivo): cubre los 5 AC contra una BD
/// real (WebApplicationFactory + FlitDbContext real, mismo patrón que
/// <see cref="Flit.Admin.Tests.OtProfile.AdminOtUsersEndpointsTests"/>): seeda su propio tenant,
/// dos roles COMPANY distintos y usuarios con GUIDs aleatorios por ejecución, y limpia lo
/// creado al finalizar.
/// </summary>
public sealed class MultiRoleUserAssignmentEndpointsTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private static readonly SymmetricSecurityKey DummyKey =
        new(Encoding.UTF8.GetBytes(new string('k', 64)));

    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _callerId = Guid.NewGuid();
    private readonly Guid _targetUserId = Guid.NewGuid();

    private Guid _roleAId;
    private Guid _roleBId;

    public MultiRoleUserAssignmentEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", MintToken("AdminCompany", _tenantId, _callerId));

        SeedAsync().GetAwaiter().GetResult();
    }

    // AC1 — asignar un segundo rol a un usuario que ya tiene uno → queda con AMBOS roles activos.
    [Fact]
    public async Task AssignRole_SecondDistinctRole_UserEndsWithBothRolesActive()
    {
        var response = await _client.PutAsJsonAsync(
            $"/api/v1/security/users/{_targetUserId}/role",
            new { roleId = _roleBId },
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        await using var db = CreateDbContext();
        var activeRoleIds = await db.UserRoleAssignments.AsNoTracking()
            .Where(a => a.UserId == _targetUserId && a.TenantId == _tenantId && a.DeletedAt == null)
            .Select(a => a.RoleId)
            .ToListAsync(TestContext.Current.CancellationToken);

        activeRoleIds.Should().BeEquivalentTo([_roleAId, _roleBId]);
    }

    // AC2 — asignar el MISMO rol ya activo → rechazo claro, sin duplicar fila.
    [Fact]
    public async Task AssignRole_SameRoleAlreadyActive_RejectsWithoutDuplicating()
    {
        var response = await _client.PutAsJsonAsync(
            $"/api/v1/security/users/{_targetUserId}/role",
            new { roleId = _roleAId },
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);

        await using var db = CreateDbContext();
        var activeCount = await db.UserRoleAssignments.AsNoTracking()
            .CountAsync(
                a => a.UserId == _targetUserId && a.TenantId == _tenantId
                     && a.RoleId == _roleAId && a.DeletedAt == null,
                TestContext.Current.CancellationToken);

        activeCount.Should().Be(1);
    }

    // AC3 — quitar un rol puntual sin afectar los demás roles del usuario.
    [Fact]
    public async Task RemoveRole_OneOfTwoActiveRoles_LeavesTheOtherUntouched()
    {
        // Deja al usuario objetivo con DOS roles activos (mismo camino que AC1) antes de quitar uno.
        await _client.PutAsJsonAsync(
            $"/api/v1/security/users/{_targetUserId}/role",
            new { roleId = _roleBId },
            TestContext.Current.CancellationToken);

        var response = await _client.DeleteAsync(
            $"/api/v1/security/users/{_targetUserId}/roles/{_roleAId}",
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        await using var db = CreateDbContext();
        var activeRoleIds = await db.UserRoleAssignments.AsNoTracking()
            .Where(a => a.UserId == _targetUserId && a.TenantId == _tenantId && a.DeletedAt == null)
            .Select(a => a.RoleId)
            .ToListAsync(TestContext.Current.CancellationToken);

        activeRoleIds.Should().BeEquivalentTo([_roleBId]);
    }

    // Sin asignación activa de ese rol puntual → 404, sin afectar el rol que sí tiene.
    [Fact]
    public async Task RemoveRole_NotActivelyAssigned_Returns404()
    {
        var response = await _client.DeleteAsync(
            $"/api/v1/security/users/{_targetUserId}/roles/{_roleBId}",
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // AC4 — invitar con varios roles simultáneos → la invitación queda con AMBAS asignaciones
    // (invitation_roles) y, al activar la cuenta, se crean AMBOS UserRoleAssignment.
    [Fact]
    public async Task InviteAndActivate_WithMultipleRoles_CreatesBothAssignmentsOnActivation()
    {
        var email = $"multi-role-invite-{Guid.NewGuid():N}@flit.local";

        var response = await _client.PostAsJsonAsync(
            "/api/v1/security/invitations",
            new { email, fullName = "Multi Rol", roleIds = new[] { _roleAId, _roleBId } },
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        Guid invitationId;
        await using (var db = CreateDbContext())
        {
            var invitation = await db.UserInvitations.AsNoTracking()
                .SingleAsync(i => i.Email == email, TestContext.Current.CancellationToken);
            invitationId = invitation.Id;

            var bridgeRoleIds = await db.InvitationRoles.AsNoTracking()
                .Where(r => r.InvitationId == invitationId)
                .Select(r => r.RoleId)
                .ToListAsync(TestContext.Current.CancellationToken);

            bridgeRoleIds.Should().BeEquivalentTo([_roleAId, _roleBId]);
        }

        // Activación: el token crudo de la invitación creada por HTTP no es recuperable desde la
        // respuesta (solo se persiste su hash) -- se crea una SEGUNDA invitación equivalente con
        // un token conocido (vía los mismos repositorios/handler de DI) para validar el efecto de
        // la activación: N asignaciones UserRoleAssignment, una por cada rol de la invitación.
        using var scope = _factory.Services.CreateScope();
        var invitationRepo = scope.ServiceProvider.GetRequiredService<IInvitationRepository>();
        var tokenGenerator = scope.ServiceProvider.GetRequiredService<ISecureTokenGenerator>();
        var activateHandler = scope.ServiceProvider.GetRequiredService<ActivateAccountHandler>();

        var activationEmail = $"activate-{Guid.NewGuid():N}@flit.local";
        var token = tokenGenerator.Generate();
        await invitationRepo.CreateAsync(
            new UserInvitationData(_tenantId, activationEmail, "Activar Multi Rol", [_roleAId, _roleBId], token.TokenHash, _callerId),
            TestContext.Current.CancellationToken);

        await activateHandler.HandleAsync(
            new ActivateAccountCommand(token.RawToken, "FlitPass1!"),
            TestContext.Current.CancellationToken);

        await using (var db = CreateDbContext())
        {
            var user = await db.Users.AsNoTracking()
                .SingleAsync(u => u.Email == activationEmail, TestContext.Current.CancellationToken);

            var assignedRoleIds = await db.UserRoleAssignments.AsNoTracking()
                .Where(a => a.UserId == user.Id && a.DeletedAt == null)
                .Select(a => a.RoleId)
                .ToListAsync(TestContext.Current.CancellationToken);

            assignedRoleIds.Should().BeEquivalentTo([_roleAId, _roleBId]);

            // Limpieza del usuario activado por este test (fuera del alcance del Dispose estándar,
            // que solo limpia por tenant_id/roleId de invitaciones/asignaciones, no por email de usuario).
            db.UserRoleAssignments.RemoveRange(db.UserRoleAssignments.Where(a => a.UserId == user.Id));
            db.UserCredentials.RemoveRange(db.UserCredentials.Where(c => c.UserId == user.Id));
            db.Users.RemoveRange(db.Users.Where(u => u.Id == user.Id));
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }
    }

    // AC5 — invitar sin ningún rol seleccionado → 400, ya no se permite "sin rol asignado".
    [Fact]
    public async Task Invite_WithoutAnyRole_Returns400()
    {
        var email = $"no-role-invite-{Guid.NewGuid():N}@flit.local";

        var response = await _client.PostAsJsonAsync(
            "/api/v1/security/invitations",
            new { email, fullName = "Sin Rol", roleIds = Array.Empty<Guid>() },
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        await using var db = CreateDbContext();
        var exists = await db.UserInvitations.AsNoTracking()
            .AnyAsync(i => i.Email == email, TestContext.Current.CancellationToken);
        exists.Should().BeFalse();
    }

    private async Task SeedAsync()
    {
        await using var db = CreateDbContext();

        db.Tenants.Add(new Tenant
        {
            Id = _tenantId,
            Code = $"CO-MULTIROLE-{Guid.NewGuid():N}"[..20],
            LegalName = "Compañía multi-rol de prueba",
            TaxId = TestNit.Unique(),
            TenantType = "RENTING",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        });

        db.Users.Add(new User
        {
            Id = _callerId,
            Email = $"caller-{_callerId:N}@flit.local",
            DisplayName = "Caller de prueba",
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

        var roleA = new Role
        {
            Id = Guid.NewGuid(),
            Code = $"MultiRoleTestA-{Guid.NewGuid():N}"[..40],
            Name = "Multi Role Test A",
            TargetEntityType = "COMPANY",
            IsSystem = false,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        var roleB = new Role
        {
            Id = Guid.NewGuid(),
            Code = $"MultiRoleTestB-{Guid.NewGuid():N}"[..40],
            Name = "Multi Role Test B",
            TargetEntityType = "COMPANY",
            IsSystem = false,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        db.Roles.AddRange(roleA, roleB);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        _roleAId = roleA.Id;
        _roleBId = roleB.Id;

        // El usuario objetivo ya tiene el rol A activo (estado previo para AC1/AC2/AC3).
        db.UserRoleAssignments.Add(new UserRoleAssignment
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantId,
            UserId = _targetUserId,
            RoleId = _roleAId,
            AssignedAt = DateTimeOffset.UtcNow,
            AssignedBy = _callerId,
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
    /// Limpieza en orden de dependencia FK: cada etapa hace su propio <c>SaveChanges</c> porque,
    /// sin navegaciones EF entre estos agregados, el batching no puede inferir el orden correcto
    /// de borrado (mismo patrón que <see cref="Flit.Admin.Tests.OtProfile.AdminOtUsersEndpointsTests"/>).
    /// </summary>
    public void Dispose()
    {
        using var db = CreateDbContext();

        db.InvitationRoles.RemoveRange(db.InvitationRoles.Where(r => r.TenantId == _tenantId));
        db.UserInvitations.RemoveRange(db.UserInvitations.Where(i => i.TenantId == _tenantId));
        db.UserRoleAssignments.RemoveRange(db.UserRoleAssignments.Where(a => a.TenantId == _tenantId));
        db.SaveChanges();

        db.Users.RemoveRange(db.Users.Where(u => u.Id == _callerId || u.Id == _targetUserId));
        db.Roles.RemoveRange(db.Roles.Where(r => r.Id == _roleAId || r.Id == _roleBId));
        db.Tenants.RemoveRange(db.Tenants.Where(t => t.Id == _tenantId));
        db.SaveChanges();
    }
}
