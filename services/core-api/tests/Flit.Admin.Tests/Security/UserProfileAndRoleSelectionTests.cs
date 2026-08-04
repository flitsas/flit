using System.Data.Common;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
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

namespace Flit.Admin.Tests.Security;

/// <summary>
/// Perfiles de usuario (context/usuarios-contex.md): el SuperAdmin puede ELEGIR el rol al
/// invitar en vez de recibir siempre el rol de sistema forzado, puede crear usuarios de perfil
/// FLIT sin compañía ni organismo destino, y el listado de usuarios expone el perfil resuelto
/// por el servidor. Integración real contra la BD de desarrollo (mismo patrón que
/// <c>SecurityInvitationsRoleResolutionTests</c>): GUIDs aleatorios por ejecución y limpieza
/// de lo creado al finalizar.
/// </summary>
public sealed class UserProfileAndRoleSelectionTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private static readonly SymmetricSecurityKey DummyKey =
        new(Encoding.UTF8.GetBytes(new string('k', 64)));

    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    private readonly Guid _superAdminUserId = Guid.NewGuid();
    private readonly Guid _flitTenantId = Guid.NewGuid();

    private readonly Guid _companyTenantId = Guid.NewGuid();
    private readonly Guid _transitOfficeId = Guid.NewGuid();
    private readonly Guid _otTenantId = Guid.NewGuid();

    private Guid _superAdminRoleId;
    private Guid _otAdminRoleId;

    // Rol NO de sistema, exclusivo de este test: prueba que la selección explícita funciona
    // con roles personalizados y no solo con los tres roles de sistema.
    private readonly Guid _customCompanyRoleId = Guid.NewGuid();
    private readonly string _customCompanyRoleCode = $"tst_{Guid.NewGuid():N}"[..16];

    private readonly Guid _customOtRoleId = Guid.NewGuid();
    private readonly string _customOtRoleCode = $"tso_{Guid.NewGuid():N}"[..16];

    // Segundo rol de compañía: el destino del cambio de rol (el usuario ya tiene el primero).
    private readonly Guid _secondCompanyRoleId = Guid.NewGuid();
    private readonly string _secondCompanyRoleCode = $"tst_{Guid.NewGuid():N}"[..16];

    // Usuario SIN home_tenant_id, con una asignación de rol activa en la compañía: la forma en
    // que existen varios usuarios reales del sistema.
    private readonly Guid _userWithoutHomeTenantId = Guid.NewGuid();
    private readonly Guid _assignmentWithoutHomeTenantId = Guid.NewGuid();

    public UserProfileAndRoleSelectionTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", MintToken("SuperAdmin", _flitTenantId, _superAdminUserId));

        SeedAsync().GetAwaiter().GetResult();
    }

    // ── Selector de rol ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Invite_AsSuperAdmin_WithExplicitCompanyRole_UsesSelectedRoleInsteadOfForcedOne()
    {
        var email = $"rol-elegido-{Guid.NewGuid():N}@flit.local";

        var response = await _client.PostAsJsonAsync(
            "/api/v1/security/invitations",
            new
            {
                email,
                fullName = "Rol elegido",
                targetTenantId = _companyTenantId,
                roleIds = new[] { _customCompanyRoleId },
            },
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        await using var db = CreateDbContext();
        var invitation = await db.UserInvitations.AsNoTracking()
            .SingleAsync(i => i.Email == email, TestContext.Current.CancellationToken);
        invitation.TenantId.Should().Be(_companyTenantId);
        invitation.RoleId.Should().Be(_customCompanyRoleId,
            "el rol seleccionado debe respetarse en vez de forzar AdminCompany");
    }

    [Fact]
    public async Task Invite_AsSuperAdmin_WithoutRoleIds_StillForcesSystemRole()
    {
        var email = $"rol-forzado-{Guid.NewGuid():N}@flit.local";

        var response = await _client.PostAsJsonAsync(
            "/api/v1/security/invitations",
            new { email, fullName = "Rol forzado", targetTenantId = _otTenantId },
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        await using var db = CreateDbContext();
        var invitation = await db.UserInvitations.AsNoTracking()
            .SingleAsync(i => i.Email == email, TestContext.Current.CancellationToken);
        invitation.RoleId.Should().Be(_otAdminRoleId, "sin selección explícita se conserva el comportamiento previo");
    }

    [Fact]
    public async Task Invite_AsSuperAdmin_WithRoleOfWrongTenantType_Returns409()
    {
        var email = $"rol-incoherente-{Guid.NewGuid():N}@flit.local";

        // Rol de organismo de tránsito aplicado a un tenant compañía.
        var response = await _client.PostAsJsonAsync(
            "/api/v1/security/invitations",
            new
            {
                email,
                fullName = "Rol incoherente",
                targetTenantId = _companyTenantId,
                roleIds = new[] { _otAdminRoleId },
            },
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Invite_AsSuperAdmin_WithUnknownRole_Returns404()
    {
        var email = $"rol-inexistente-{Guid.NewGuid():N}@flit.local";

        var response = await _client.PostAsJsonAsync(
            "/api/v1/security/invitations",
            new
            {
                email,
                fullName = "Rol inexistente",
                targetTenantId = _companyTenantId,
                roleIds = new[] { Guid.NewGuid() },
            },
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── Perfil FLIT ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Invite_AsSuperAdmin_WithSuperAdminRole_DoesNotRequireCompanyOrTransitOffice()
    {
        var email = $"flit-{Guid.NewGuid():N}@flit.local";

        // Sin targetTenantId: el perfil FLIT no pertenece a ninguna compañía ni organismo.
        var response = await _client.PostAsJsonAsync(
            "/api/v1/security/invitations",
            new { email, fullName = "Nuevo SuperAdmin", roleIds = new[] { _superAdminRoleId } },
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        await using var db = CreateDbContext();
        var invitation = await db.UserInvitations.AsNoTracking()
            .SingleAsync(i => i.Email == email, TestContext.Current.CancellationToken);
        invitation.TenantId.Should().Be(_flitTenantId, "el usuario FLIT vive en el tenant interno del propio SuperAdmin");
        invitation.RoleId.Should().Be(_superAdminRoleId);
    }

    [Fact]
    public async Task Invite_AsSuperAdmin_WithoutRoleAndWithoutTenant_StillReturns400()
    {
        var email = $"sin-destino-{Guid.NewGuid():N}@flit.local";

        var response = await _client.PostAsJsonAsync(
            "/api/v1/security/invitations",
            new { email, fullName = "Sin destino" },
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "sin rol FLIT explícito el destino sigue siendo obligatorio");
    }

    [Fact]
    public async Task Invite_AsSuperAdmin_WithSuperAdminRoleAndForeignTenant_Returns400()
    {
        var email = $"flit-con-tenant-{Guid.NewGuid():N}@flit.local";

        var response = await _client.PostAsJsonAsync(
            "/api/v1/security/invitations",
            new
            {
                email,
                fullName = "SuperAdmin con compañía",
                targetTenantId = _companyTenantId,
                roleIds = new[] { _superAdminRoleId },
            },
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Invite_AsSuperAdmin_WithSuperAdminRolePlusAnotherRole_Returns400()
    {
        var email = $"flit-multirol-{Guid.NewGuid():N}@flit.local";

        var response = await _client.PostAsJsonAsync(
            "/api/v1/security/invitations",
            new
            {
                email,
                fullName = "SuperAdmin multirol",
                roleIds = new[] { _superAdminRoleId, _customCompanyRoleId },
            },
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ── Alta de usuario OT con rol seleccionable ──────────────────────────────────────

    [Fact]
    public async Task InviteOtUser_WithExplicitTransitOfficeRole_UsesSelectedRole()
    {
        var email = $"ot-rol-elegido-{Guid.NewGuid():N}@flit.local";

        var response = await _client.PostAsJsonAsync(
            $"/api/v1/admin/ot/users/invite?transitOfficeId={_transitOfficeId}",
            new { email, fullName = "Revisor OT", roleIds = new[] { _customOtRoleId } },
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        await using var db = CreateDbContext();
        var invitation = await db.UserInvitations.AsNoTracking()
            .SingleAsync(i => i.Email == email, TestContext.Current.CancellationToken);
        invitation.RoleId.Should().Be(_customOtRoleId, "el alta OT ya no fuerza siempre ot_admin");
    }

    [Fact]
    public async Task InviteOtUser_WithoutRoleIds_StillForcesOtAdmin()
    {
        var email = $"ot-rol-forzado-{Guid.NewGuid():N}@flit.local";

        var response = await _client.PostAsJsonAsync(
            $"/api/v1/admin/ot/users/invite?transitOfficeId={_transitOfficeId}",
            new { email, fullName = "Admin OT por defecto" },
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        await using var db = CreateDbContext();
        var invitation = await db.UserInvitations.AsNoTracking()
            .SingleAsync(i => i.Email == email, TestContext.Current.CancellationToken);
        invitation.RoleId.Should().Be(_otAdminRoleId);
    }

    [Fact]
    public async Task InviteOtUser_WithCompanyRole_Returns409()
    {
        var email = $"ot-rol-incoherente-{Guid.NewGuid():N}@flit.local";

        var response = await _client.PostAsJsonAsync(
            $"/api/v1/admin/ot/users/invite?transitOfficeId={_transitOfficeId}",
            new { email, fullName = "Rol de compañía en OT", roleIds = new[] { _customCompanyRoleId } },
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    // ── Perfil expuesto en el listado ─────────────────────────────────────────────────

    [Fact]
    public async Task ListUsers_AsSuperAdmin_ExposesProfilePerRow()
    {
        // Una invitación por cada perfil, para verificar los tres valores en una sola pasada.
        var gestorEmail = $"perfil-gestor-{Guid.NewGuid():N}@flit.local";
        var otEmail = $"perfil-ot-{Guid.NewGuid():N}@flit.local";
        var flitEmail = $"perfil-flit-{Guid.NewGuid():N}@flit.local";

        await _client.PostAsJsonAsync(
            "/api/v1/security/invitations",
            new { email = gestorEmail, fullName = "Perfil Gestor", targetTenantId = _companyTenantId },
            TestContext.Current.CancellationToken);
        await _client.PostAsJsonAsync(
            "/api/v1/security/invitations",
            new { email = otEmail, fullName = "Perfil OT", targetTenantId = _otTenantId },
            TestContext.Current.CancellationToken);
        await _client.PostAsJsonAsync(
            "/api/v1/security/invitations",
            new { email = flitEmail, fullName = "Perfil FLIT", roleIds = new[] { _superAdminRoleId } },
            TestContext.Current.CancellationToken);

        var response = await _client.GetAsync("/api/v1/security/users", TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var payload = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using var doc = JsonDocument.Parse(payload);
        var rows = doc.RootElement.EnumerateArray().ToList();

        ProfileOf(rows, gestorEmail).Should().Be("GESTOR");
        ProfileOf(rows, otEmail).Should().Be("OT");
        ProfileOf(rows, flitEmail).Should().Be("FLIT",
            "las invitaciones al tenant interno deben aparecer y clasificarse como perfil FLIT");
    }

    // ── El rol SuperAdmin no sale del perfil FLIT ─────────────────────────────────────
    // Su TargetEntityType es COMPANY (el CHECK de BD no admite un valor propio), así que las
    // validaciones de coherencia lo dan por bueno dentro de cualquier compañía: sin guarda
    // explícita un AdminCompany podría crearse un SuperAdmin en su propia empresa.

    [Fact]
    public async Task Invite_AsCompanyAdmin_WithSuperAdminRole_IsRejected()
    {
        using var client = CreateClientAs("AdminCompany", _companyTenantId, Guid.NewGuid());
        var email = $"escalada-{Guid.NewGuid():N}@flit.local";

        var response = await client.PostAsJsonAsync(
            "/api/v1/security/invitations",
            new { email, fullName = "Escalada", roleIds = new[] { _superAdminRoleId } },
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))
            .Should().Contain("SUPERADMIN_ROLE_NOT_ASSIGNABLE");

        await using var db = CreateDbContext();
        (await db.UserInvitations.AsNoTracking()
            .AnyAsync(i => i.Email == email, TestContext.Current.CancellationToken))
            .Should().BeFalse("la invitación no debe llegar a crearse");
    }

    [Fact]
    public async Task AssignRole_AsCompanyAdmin_WithSuperAdminRole_IsRejected()
    {
        using var client = CreateClientAs("AdminCompany", _companyTenantId, Guid.NewGuid());

        var response = await client.PutAsJsonAsync(
            $"/api/v1/security/users/{Guid.NewGuid()}/role",
            new { roleId = _superAdminRoleId },
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))
            .Should().Contain("SUPERADMIN_ROLE_NOT_ASSIGNABLE");
    }

    [Fact]
    public async Task AssignRole_AsOtAdmin_WithSuperAdminRole_IsRejected()
    {
        using var client = CreateClientAs("ot_admin", _otTenantId, Guid.NewGuid());

        var response = await client.PutAsJsonAsync(
            $"/api/v1/security/users/{Guid.NewGuid()}/role",
            new { roleId = _superAdminRoleId },
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))
            .Should().Contain("SUPERADMIN_ROLE_NOT_ASSIGNABLE");
    }

    // ── Alcance sobre usuarios sin home_tenant_id ─────────────────────────────────────
    // home_tenant_id es opcional en BD: hay usuarios legítimos que solo tienen asignaciones de
    // rol (los del seeder de desarrollo, entre otros). Mirar solo esa columna hacía que editarlos
    // devolviera 404 y que cambiarles el rol devolviera OUT_OF_SCOPE.

    [Fact]
    public async Task UpdateUser_WithoutHomeTenant_ResolvesTenantFromRoleAssignment()
    {
        var rowVersion = await GetRowVersionAsync(_userWithoutHomeTenantId);

        var response = await _client.PatchAsJsonAsync(
            $"/api/v1/security/users/{_userWithoutHomeTenantId}",
            new { displayName = "Nombre editado sin home tenant", rowVersion },
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        await using var db = CreateDbContext();
        var name = await db.Users.AsNoTracking()
            .Where(u => u.Id == _userWithoutHomeTenantId)
            .Select(u => u.DisplayName)
            .SingleAsync(TestContext.Current.CancellationToken);
        name.Should().Be("Nombre editado sin home tenant");
    }

    [Fact]
    public async Task AssignRole_AsSuperAdmin_AppliesInTargetUserTenantNotInCallerTenant()
    {
        var response = await _client.PutAsJsonAsync(
            $"/api/v1/security/users/{_userWithoutHomeTenantId}/role",
            new { roleId = _secondCompanyRoleId },
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "el SuperAdmin opera en el tenant del usuario destino, no en su tenant interno de FLIT");

        await using var db = CreateDbContext();
        var assignment = await db.UserRoleAssignments.AsNoTracking()
            .SingleAsync(
                a => a.UserId == _userWithoutHomeTenantId && a.RoleId == _secondCompanyRoleId && a.DeletedAt == null,
                TestContext.Current.CancellationToken);
        assignment.TenantId.Should().Be(_companyTenantId);
    }

    private async Task<long> GetRowVersionAsync(Guid userId)
    {
        await using var db = CreateDbContext();
        return await db.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => u.RowVersion)
            .SingleAsync(TestContext.Current.CancellationToken);
    }

    // ── Auditoría legible ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task AuditLog_ResolvesActorNameInsteadOfReturningOnlyUuid()
    {
        var email = $"auditoria-{Guid.NewGuid():N}@flit.local";

        var invite = await _client.PostAsJsonAsync(
            "/api/v1/security/invitations",
            new { email, fullName = "Usuario auditado", targetTenantId = _companyTenantId },
            TestContext.Current.CancellationToken);
        invite.StatusCode.Should().Be(HttpStatusCode.Created);

        var response = await _client.GetAsync(
            $"/api/v1/superadmin/audit?userId={_superAdminUserId}&pageSize=10",
            TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var payload = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using var doc = JsonDocument.Parse(payload);
        var entries = doc.RootElement.GetProperty("data").EnumerateArray().ToList();

        entries.Should().NotBeEmpty();
        entries.Should().Contain(
            e => e.GetProperty("changedByName").GetString() == "SuperAdmin perfiles",
            "el rastro debe identificar al actor por nombre y no solo por su UUID");
    }

    private static string? ProfileOf(List<JsonElement> rows, string email) =>
        rows.Where(r => r.GetProperty("email").GetString() == email)
            .Select(r => r.GetProperty("profile").GetString())
            .FirstOrDefault();

    // ── Fixture ───────────────────────────────────────────────────────────────────────

    private async Task SeedAsync()
    {
        await using var db = CreateDbContext();

        db.Tenants.Add(new Tenant
        {
            Id = _flitTenantId,
            Code = $"FL-TEST-{Guid.NewGuid():N}"[..20],
            LegalName = "FLIT interno de prueba",
            TaxId = TestNit.Unique(),
            TenantType = "RENTING",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        });

        db.Tenants.Add(new Tenant
        {
            Id = _companyTenantId,
            Code = $"CO-TEST-{Guid.NewGuid():N}"[..20],
            LegalName = "Compañía de prueba (perfiles)",
            TaxId = TestNit.Unique(),
            TenantType = "RENTING",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        });

        db.Tenants.Add(new Tenant
        {
            Id = _otTenantId,
            Code = $"OT-TEST-{Guid.NewGuid():N}"[..20],
            LegalName = "OT de prueba (perfiles)",
            TaxId = TestNit.Unique(),
            TenantType = "RENTING",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        });

        db.TransitOffices.Add(new TransitOffice
        {
            Id = _transitOfficeId,
            Code = $"P{Guid.NewGuid():N}"[..10],
            Name = "OT perfiles tests",
            DepartmentCode = "99",
            CityCode = "99997",
            IsActive = true,
        });

        db.Users.Add(new User
        {
            Id = _superAdminUserId,
            Email = $"sa-perfiles-{_superAdminUserId:N}@flit.local",
            DisplayName = "SuperAdmin perfiles",
            Status = "active",
            CreatedAt = DateTimeOffset.UtcNow,
        });

        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        // HU #10505 / ADR-0023: catálogo GLOBAL de roles — se reutiliza la fila si ya existe.
        _superAdminRoleId = await GetOrCreateGlobalRoleAsync(
            db, "SuperAdmin", "Super Administrador", "COMPANY", TestContext.Current.CancellationToken);
        _otAdminRoleId = await GetOrCreateGlobalRoleAsync(
            db, "ot_admin", "Administrador OT", "TRANSIT_OFFICE", TestContext.Current.CancellationToken);

        db.Roles.Add(new Role
        {
            Id = _customCompanyRoleId,
            Code = _customCompanyRoleCode,
            Name = "Radicador de prueba",
            TargetEntityType = "COMPANY",
            IsSystem = false,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        });

        db.Roles.Add(new Role
        {
            Id = _secondCompanyRoleId,
            Code = _secondCompanyRoleCode,
            Name = "Segundo rol de prueba",
            TargetEntityType = "COMPANY",
            IsSystem = false,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        });

        db.Roles.Add(new Role
        {
            Id = _customOtRoleId,
            Code = _customOtRoleCode,
            Name = "Revisor documental de prueba",
            TargetEntityType = "TRANSIT_OFFICE",
            IsSystem = false,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        });

        // Usuario deliberadamente SIN HomeTenantId: su único vínculo con la compañía es la
        // asignación de rol, igual que varios usuarios reales del sistema.
        db.Users.Add(new User
        {
            Id = _userWithoutHomeTenantId,
            Email = $"sin-home-{_userWithoutHomeTenantId:N}@flit.local",
            DisplayName = "Usuario sin home tenant",
            Status = "active",
            CreatedAt = DateTimeOffset.UtcNow,
        });

        db.UserRoleAssignments.Add(new UserRoleAssignment
        {
            Id = _assignmentWithoutHomeTenantId,
            TenantId = _companyTenantId,
            UserId = _userWithoutHomeTenantId,
            RoleId = _customCompanyRoleId,
            AssignedBy = _superAdminUserId,
            AssignedAt = DateTimeOffset.UtcNow,
        });

        db.TransitOfficeProfiles.Add(new TransitOfficeProfile
        {
            Id = Guid.NewGuid(),
            TenantId = _otTenantId,
            TransitOfficeId = _transitOfficeId,
            OperationMode = "dashboard",
            CreatedAt = DateTimeOffset.UtcNow,
        });

        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>Cliente autenticado con otro rol/tenant que el SuperAdmin del fixture.</summary>
    private HttpClient CreateClientAs(string role, Guid tenantId, Guid userId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", MintToken(role, tenantId, userId));
        return client;
    }

    private FlitDbContext CreateDbContext() =>
        _factory.Services.CreateScope().ServiceProvider.GetRequiredService<FlitDbContext>();

    private static async Task<Guid> GetOrCreateGlobalRoleAsync(
        FlitDbContext db, string code, string name, string targetEntityType, CancellationToken ct)
    {
        var existingId = await db.Roles.AsNoTracking()
            .Where(r => r.Code == code && r.TargetEntityType == targetEntityType && r.DeletedAt == null)
            .Select(r => r.Id)
            .FirstOrDefaultAsync(ct);

        if (existingId != Guid.Empty)
            return existingId;

        var role = new Role
        {
            Id = Guid.NewGuid(),
            Code = code,
            Name = name,
            TargetEntityType = targetEntityType,
            IsSystem = true,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        db.Roles.Add(role);
        await db.SaveChangesAsync(ct);
        return role.Id;
    }

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
    /// Limpieza en orden de dependencia FK, con un <c>SaveChanges</c> por etapa (sin
    /// navegaciones EF entre estos agregados el batching no infiere el orden de borrado).
    /// Solo se borran los roles personalizados: los de sistema son del catálogo global.
    /// </summary>
    /// <remarks>
    /// Cada etapa va aislada en su propio try: la BD de desarrollo es COMPARTIDA, así que si una
    /// etapa falla las siguientes deben ejecutarse igual. Sin este aislamiento, un fallo al
    /// borrar el usuario arrastraba en el mismo lote el borrado de los roles y dejaba
    /// "Radicador de prueba" visible en el selector de roles de la aplicación real.
    /// </remarks>
    public void Dispose()
    {
        using var db = CreateDbContext();

        TryDelete(() => db.UserInvitations
            .Where(i => i.TenantId == _companyTenantId || i.TenantId == _otTenantId || i.TenantId == _flitTenantId)
            .ExecuteDelete());
        TryDelete(() => db.TransitOfficeProfiles.Where(p => p.TenantId == _otTenantId).ExecuteDelete());

        // Antes que los roles: las asignaciones los referencian.
        TryDelete(() => db.UserRoleAssignments
            .Where(a => a.UserId == _userWithoutHomeTenantId)
            .ExecuteDelete());

        // Los roles van en su PROPIA etapa: son lo único que queda visible en la UI si sobrevive.
        TryDelete(() => db.Roles
            .Where(r => r.Id == _customCompanyRoleId || r.Id == _customOtRoleId || r.Id == _secondCompanyRoleId)
            .ExecuteDelete());

        // Cada invitación deja una fila de auditoría con changed_by = SuperAdmin
        // (tenant_config_audit_logs_changed_by_fkey), que bloquea el borrado del usuario. Va
        // pegado al borrado del usuario y en el mismo reintento: la traza se escribe en su
        // propio scope/transacción (AdminAuditWriter), así que una fila puede confirmarse justo
        // después de este DELETE y dejar al usuario sin poder borrarse en la BD compartida.
        TryDelete(() =>
        {
            db.TenantConfigAuditLogs
                .Where(a => a.ChangedBy == _superAdminUserId || a.TargetEntityId == _userWithoutHomeTenantId)
                .ExecuteDelete();
            db.Users
                .Where(u => u.Id == _superAdminUserId || u.Id == _userWithoutHomeTenantId)
                .ExecuteDelete();
        });

        TryDelete(() => db.Tenants
            .Where(t => t.Id == _companyTenantId || t.Id == _otTenantId || t.Id == _flitTenantId)
            .ExecuteDelete());
        TryDelete(() => db.TransitOffices.Where(o => o.Id == _transitOfficeId).ExecuteDelete());
    }

    /// <summary>
    /// Ejecuta una etapa de limpieza sin dejar que su fallo aborte las siguientes ni haga fallar
    /// el test (el test ya pasó: esto es housekeeping). Se reintenta una vez porque la BD de
    /// desarrollo es COMPARTIDA y una escritura concurrente puede volver a bloquear el borrado.
    /// Se usa <c>ExecuteDelete</c> (SQL directo) para no arrastrar entidades entre etapas.
    /// </summary>
    private static void TryDelete(Action stage)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                stage();
                return;
            }
            catch (DbUpdateException)
            {
                // Reintento: la etapa siguiente debe correr igual aunque esta no lo consiga.
            }
            catch (DbException)
            {
                // ExecuteDelete no envuelve el error del proveedor (p. ej. violación de FK).
            }
        }
    }
}
