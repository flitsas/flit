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
    private readonly Guid _companyAdminRoleId = Guid.NewGuid();

    private readonly Guid _transitOfficeId = Guid.NewGuid();
    private readonly Guid _otTenantId = Guid.NewGuid();
    private readonly Guid _otAdminRoleId = Guid.NewGuid();

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

        // Tenants + Users primero (el Role y el perfil OT dependen del tenant recién
        // insertado; sin navegaciones EF entre estos agregados, mismo motivo que en
        // AdminOtUsersEndpointsTests.SeedAsync).
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        db.Roles.Add(new Role
        {
            Id = _companyAdminRoleId,
            TenantId = _companyTenantId,
            Code = "AdminCompany",
            Name = "Administrador de Compañía",
            IsSystem = true,
            CreatedAt = DateTimeOffset.UtcNow,
            RowVersion = 0,
        });

        db.Roles.Add(new Role
        {
            Id = _otAdminRoleId,
            TenantId = _otTenantId,
            Code = "ot_admin",
            Name = "Administrador OT",
            IsSystem = true,
            CreatedAt = DateTimeOffset.UtcNow,
            RowVersion = 0,
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

        db.Roles.RemoveRange(
            db.Roles.Where(r => r.TenantId == _companyTenantId || r.TenantId == _otTenantId));
        db.SaveChanges();

        db.Users.RemoveRange(db.Users.Where(u => u.Id == _superAdminUserId));
        db.Tenants.RemoveRange(
            db.Tenants.Where(t => t.Id == _companyTenantId || t.Id == _otTenantId));
        db.TransitOffices.RemoveRange(db.TransitOffices.Where(o => o.Id == _transitOfficeId));
        db.SaveChanges();
    }
}
