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
/// <c>POST /api/v1/security/invitations</c> cuando el caller es SuperAdmin (refactor
/// adminOT): el rol de sistema a asignar ya no se fuerza siempre a <c>AdminCompany</c> —
/// se resuelve según el tipo de tenant destino (OT con <see cref="TransitOfficeProfile"/>
/// asociado → <c>ot_admin</c>; compañía → <c>AdminCompany</c>, comportamiento sin cambios).
/// Integración real contra la BD de desarrollo (WebApplicationFactory + FlitDbContext real,
/// mismo patrón que <see cref="AdminOtUsersEndpointsTests"/>): seeda un tenant compañía y un
/// tenant OT con GUIDs aleatorios por ejecución, y limpia lo creado al finalizar.
/// </summary>
public sealed class SecurityInvitationsRoleResolutionTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private static readonly SymmetricSecurityKey DummyKey =
        new(Encoding.UTF8.GetBytes(new string('k', 64)));

    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    private readonly Guid _superAdminUserId = Guid.NewGuid();
    private readonly Guid _superAdminTenantId = Guid.NewGuid();

    private readonly Guid _companyTenantId = Guid.NewGuid();

    // HU #10505: security.roles es un catálogo GLOBAL — "AdminCompany"/"ot_admin" ya no se
    // crean por tenant en el seed de este test; se resuelven por Code contra las filas
    // globales sembradas por DevelopmentAuthSeeder al levantar la WebApplicationFactory.
    private Guid _companyAdminRoleId;

    private readonly Guid _transitOfficeId = Guid.NewGuid();
    private readonly Guid _otTenantId = Guid.NewGuid();
    private Guid _otAdminRoleId;

    public SecurityInvitationsRoleResolutionTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", MintToken("SuperAdmin", _superAdminTenantId, _superAdminUserId));

        SeedAsync().GetAwaiter().GetResult();
    }

    [Fact]
    public async Task Invite_AsSuperAdmin_TargetingCompanyTenant_ResolvesAdminCompanyRole()
    {
        var email = $"admin-company-invite-{Guid.NewGuid():N}@flit.local";

        var response = await _client.PostAsJsonAsync(
            "/api/v1/security/invitations",
            new { email, fullName = "Admin Compañía", targetTenantId = _companyTenantId },
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        await using var db = CreateDbContext();
        var invitation = await db.UserInvitations.AsNoTracking()
            .SingleAsync(i => i.Email == email, TestContext.Current.CancellationToken);
        invitation.TenantId.Should().Be(_companyTenantId);
        invitation.RoleId.Should().Be(_companyAdminRoleId);
    }

    [Fact]
    public async Task Invite_AsSuperAdmin_TargetingOtTenant_ResolvesOtAdminRole()
    {
        var email = $"admin-ot-invite-{Guid.NewGuid():N}@flit.local";

        var response = await _client.PostAsJsonAsync(
            "/api/v1/security/invitations",
            new { email, fullName = "Admin OT", targetTenantId = _otTenantId },
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        await using var db = CreateDbContext();
        var invitation = await db.UserInvitations.AsNoTracking()
            .SingleAsync(i => i.Email == email, TestContext.Current.CancellationToken);
        invitation.TenantId.Should().Be(_otTenantId);
        invitation.RoleId.Should().Be(_otAdminRoleId);
    }

    // HU #10625 AC1 — SuperAdmin puede reenviar CUALQUIER invitación del sistema (sin
    // restricción de tenant), a diferencia de AdminCompany que solo puede su propio tenant.
    [Fact]
    public async Task Resend_AsSuperAdmin_InvitationOfAnotherTenant_Returns200()
    {
        var (invitationId, email) = await SeedPendingInvitationAsync(_otTenantId, lastSentAt: null);

        var response = await _client.PostAsync(
            $"/api/v1/security/invitations/{invitationId}/resend", null, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        await using var db = CreateDbContext();
        var invitation = await db.UserInvitations.AsNoTracking()
            .SingleAsync(i => i.Id == invitationId, TestContext.Current.CancellationToken);
        invitation.Email.Should().Be(email);
        invitation.LastSentAt.Should().NotBeNull();
    }

    // HU #10625 AC1 — AdminCompany reenvía una invitación pendiente de su propio tenant
    [Fact]
    public async Task Resend_AsAdminCompany_OwnTenantInvitation_Returns200()
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", MintToken("AdminCompany", _companyTenantId, Guid.NewGuid()));

        var (invitationId, _) = await SeedPendingInvitationAsync(_companyTenantId, lastSentAt: null);

        var response = await _client.PostAsync(
            $"/api/v1/security/invitations/{invitationId}/resend", null, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // HU #10625 AC2 — cooldown activo: reenviada hace menos de 2 minutos → 429 + Retry-After
    [Fact]
    public async Task Resend_AsAdminCompany_WithinCooldown_Returns429WithRetryAfterHeader()
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", MintToken("AdminCompany", _companyTenantId, Guid.NewGuid()));

        var (invitationId, _) = await SeedPendingInvitationAsync(
            _companyTenantId, lastSentAt: DateTimeOffset.UtcNow.AddSeconds(-30));

        var response = await _client.PostAsync(
            $"/api/v1/security/invitations/{invitationId}/resend", null, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        response.Headers.RetryAfter.Should().NotBeNull();
    }

    // HU #10625 AC3 — invitación ya cancelada → 409
    [Fact]
    public async Task Resend_AsAdminCompany_InvitationCancelled_Returns409()
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", MintToken("AdminCompany", _companyTenantId, Guid.NewGuid()));

        var (invitationId, _) = await SeedPendingInvitationAsync(
            _companyTenantId, lastSentAt: null, status: "cancelled");

        var response = await _client.PostAsync(
            $"/api/v1/security/invitations/{invitationId}/resend", null, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    // AdminCompany no puede reenviar una invitación de OTRO tenant → 404 (fuera de alcance)
    [Fact]
    public async Task Resend_AsAdminCompany_InvitationOfAnotherTenant_Returns404()
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", MintToken("AdminCompany", _companyTenantId, Guid.NewGuid()));

        var (invitationId, _) = await SeedPendingInvitationAsync(_otTenantId, lastSentAt: null);

        var response = await _client.PostAsync(
            $"/api/v1/security/invitations/{invitationId}/resend", null, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private async Task<(Guid InvitationId, string Email)> SeedPendingInvitationAsync(
        Guid tenantId, DateTimeOffset? lastSentAt, string status = "pending")
    {
        await using var db = CreateDbContext();

        var invitationId = Guid.NewGuid();
        var email = $"resend-{Guid.NewGuid():N}@flit.local";

        db.UserInvitations.Add(new UserInvitation
        {
            Id = invitationId,
            TenantId = tenantId,
            Email = email,
            FullName = "Invitado Reenvío",
            TokenHash = $"hash-{Guid.NewGuid():N}",
            Status = status,
            InvitedBy = _superAdminUserId,
            CreatedAt = DateTimeOffset.UtcNow,
            LastSentAt = lastSentAt,
            RowVersion = 0,
        });

        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        return (invitationId, email);
    }

    private async Task SeedAsync()
    {
        await using var db = CreateDbContext();

        db.Tenants.Add(new Tenant
        {
            Id = _companyTenantId,
            Code = $"CO-TEST-{Guid.NewGuid():N}"[..20],
            LegalName = "Compañía de prueba",
            TaxId = "900888888-8",
            TenantType = "RENTING",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        });

        db.TransitOffices.Add(new TransitOffice
        {
            Id = _transitOfficeId,
            Code = $"T{Guid.NewGuid():N}"[..10],
            Name = "OT invitations role-resolution tests",
            DepartmentCode = "99",
            CityCode = "99998",
            IsActive = true,
        });

        db.Tenants.Add(new Tenant
        {
            Id = _otTenantId,
            Code = $"OT-TEST-{Guid.NewGuid():N}"[..20],
            LegalName = "OT de prueba",
            TaxId = "900777777-7",
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

        // Tenants + Users primero (el perfil OT depende del tenant recién insertado; sin
        // navegaciones EF entre estos agregados, mismo motivo que en
        // AdminOtUsersEndpointsTests.SeedAsync).
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        // HU #10505 / ADR-0023: security.roles es un catálogo GLOBAL (sin tenant_id) — se
        // reutiliza la fila si ya existe (p.ej. sembrada por DevelopmentAuthSeeder) o se crea
        // aquí mismo si no (BD limpia, como la que usa CI): el test no puede depender de que
        // el seeder de desarrollo haya corrido antes que este, solo de que exista una única
        // fila global (violaría UNIQUE(code, target_entity_type) crear una por tenant de prueba).
        _companyAdminRoleId = await GetOrCreateGlobalRoleAsync(
            db, "AdminCompany", "Administrador de Compañía", "COMPANY", TestContext.Current.CancellationToken);

        _otAdminRoleId = await GetOrCreateGlobalRoleAsync(
            db, "ot_admin", "Administrador OT", "TRANSIT_OFFICE", TestContext.Current.CancellationToken);

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
            .SingleOrDefaultAsync(ct);

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
    /// Limpieza en orden de dependencia FK: cada etapa hace su propio <c>SaveChanges</c>
    /// porque, sin navegaciones EF entre estos agregados, el batching no puede inferir el
    /// orden correcto de borrado (mismo patrón que <see cref="AdminOtUsersEndpointsTests"/>).
    /// </summary>
    public void Dispose()
    {
        using var db = CreateDbContext();

        db.UserInvitations.RemoveRange(
            db.UserInvitations.Where(i => i.TenantId == _companyTenantId || i.TenantId == _otTenantId));
        db.TransitOfficeProfiles.RemoveRange(db.TransitOfficeProfiles.Where(p => p.TenantId == _otTenantId));
        db.SaveChanges();

        // HU #10505: "AdminCompany"/"ot_admin" son roles del catálogo GLOBAL, sembrados una sola
        // vez por DevelopmentAuthSeeder — este test NO los crea ni los borra, solo los resuelve.

        db.Users.RemoveRange(db.Users.Where(u => u.Id == _superAdminUserId));
        db.Tenants.RemoveRange(
            db.Tenants.Where(t => t.Id == _companyTenantId || t.Id == _otTenantId));
        db.TransitOffices.RemoveRange(db.TransitOffices.Where(o => o.Id == _transitOfficeId));
        db.SaveChanges();
    }
}
