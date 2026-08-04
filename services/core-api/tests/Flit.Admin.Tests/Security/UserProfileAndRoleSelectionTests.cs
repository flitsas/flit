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
            Id = _customOtRoleId,
            Code = _customOtRoleCode,
            Name = "Revisor documental de prueba",
            TargetEntityType = "TRANSIT_OFFICE",
            IsSystem = false,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
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

        TryDelete(db, () =>
        {
            db.UserInvitations.RemoveRange(db.UserInvitations.Where(
                i => i.TenantId == _companyTenantId || i.TenantId == _otTenantId || i.TenantId == _flitTenantId));
            db.TransitOfficeProfiles.RemoveRange(db.TransitOfficeProfiles.Where(p => p.TenantId == _otTenantId));
        });

        // Cada invitación deja una fila de auditoría con changed_by = SuperAdmin
        // (tenant_config_audit_logs_changed_by_fkey), que bloquea el borrado del usuario.
        TryDelete(db, () => db.TenantConfigAuditLogs.RemoveRange(
            db.TenantConfigAuditLogs.Where(a => a.ChangedBy == _superAdminUserId)));

        // Los roles van en su PROPIA etapa: son lo único que queda visible en la UI si sobrevive.
        TryDelete(db, () => db.Roles.RemoveRange(db.Roles.Where(
            r => r.Id == _customCompanyRoleId || r.Id == _customOtRoleId)));

        TryDelete(db, () => db.Users.RemoveRange(db.Users.Where(u => u.Id == _superAdminUserId)));

        TryDelete(db, () =>
        {
            db.Tenants.RemoveRange(db.Tenants.Where(
                t => t.Id == _companyTenantId || t.Id == _otTenantId || t.Id == _flitTenantId));
            db.TransitOffices.RemoveRange(db.TransitOffices.Where(o => o.Id == _transitOfficeId));
        });
    }

    /// <summary>
    /// Ejecuta una etapa de limpieza sin dejar que su fallo aborte las siguientes ni haga
    /// fallar el test (el test ya pasó: esto es housekeeping). Descarta el change tracker para
    /// que las entidades de una etapa fallida no se reintenten en la siguiente.
    /// </summary>
    private static void TryDelete(FlitDbContext db, Action stage)
    {
        try
        {
            stage();
            db.SaveChanges();
        }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
        }
    }
}
