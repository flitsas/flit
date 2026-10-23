using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using Flit.Api.Authorization;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Admin;
using Flit.Infrastructure.Persistence.Entities.Catalogs;
using Flit.Infrastructure.Persistence.Entities.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace Flit.Admin.Tests.Plataforma.Mandatos;

/// <summary>
/// Arnés HTTP compartido por las pruebas de las reglas de mandato por compañía y organismo del Super Admin
/// (Feature #13117, F5): HU #13148 (concurrencia), #13149 (bitácora y acceso) y #13154 (efecto del tipo).
/// Siembra en la base local un organismo O, una compañía C con grant habilitado en O, una compañía D SIN grant,
/// el Super Admin de Plataforma (en otro tenant), un ot_admin de O y un AdminCompany de C; y limpia al terminar.
/// <para>Uso de ejemplo: <c>await Client.PutAsJsonAsync(RuleUrl(Company), new { assignmentMode = "open" })</c>
/// con <see cref="AuthenticateSuperAdmin"/>.</para>
/// </summary>
public abstract class CompanyRulesHttpTestBase : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private static readonly SymmetricSecurityKey DummyKey =
        new(Encoding.UTF8.GetBytes(new string('k', 64)));

    protected WebApplicationFactory<Program> Factory { get; }
    protected HttpClient Client { get; }

    protected Guid Office { get; } = Guid.NewGuid();
    protected Guid Company { get; } = Guid.NewGuid();
    protected Guid CompanyWithoutGrant { get; } = Guid.NewGuid();
    protected Guid SuperAdminTenantId { get; } = Guid.NewGuid();
    protected Guid SuperAdminUserId { get; } = Guid.NewGuid();
    protected Guid OtTenantId { get; } = Guid.NewGuid();
    protected Guid OtUserId { get; } = Guid.NewGuid();
    protected Guid CompanyAdminUserId { get; } = Guid.NewGuid();

    protected static CancellationToken Ct => TestContext.Current.CancellationToken;

    protected CompanyRulesHttpTestBase(WebApplicationFactory<Program> factory)
    {
        Factory = factory;
        Client = factory.CreateClient();
        SeedAsync().GetAwaiter().GetResult();
    }

    protected string RuleUrl(Guid company) =>
        $"/api/v1/admin/plataforma/mandatos/ot/{Office}/company-rules/{company}";

    protected FlitDbContext CreateDbContext() =>
        Factory.Services.CreateScope().ServiceProvider.GetRequiredService<FlitDbContext>();

    protected void AuthenticateSuperAdmin() =>
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", MintToken("SuperAdmin", SuperAdminTenantId, SuperAdminUserId));

    protected void AuthenticateOtAdmin() =>
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", MintToken(
                AdminAuthorization.OtAdminRole, OtTenantId, OtUserId, AdminAuthorization.TransitOfficeEntityType));

    protected void AuthenticateCompanyAdmin() =>
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", MintToken(AdminAuthorization.AdminCompanyRole, Company, CompanyAdminUserId));

    protected void AuthenticateAnonymous() => Client.DefaultRequestHeaders.Authorization = null;

    /// <summary>Crea la regla propia de la compañía por la ruta pública y devuelve su RowVersion inicial.</summary>
    protected async Task<CompanyRuleSnapshot> ReadRuleAsync(Guid company)
    {
        await using var db = CreateDbContext();
        var rule = await db.CompanyOtMandateRules.AsNoTracking()
            .FirstOrDefaultAsync(r => r.TransitOfficeId == Office && r.CompanyTenantId == company, Ct);
        return new CompanyRuleSnapshot(rule is not null, rule?.AssignmentMode, rule?.RowVersion ?? 0);
    }

    /// <summary>Filas de bitácora de una compañía en este organismo (mandatos), de la más vieja a la más nueva.</summary>
    protected async Task<List<TenantConfigAuditLog>> ReadAuditAsync(Guid company)
    {
        await using var db = CreateDbContext();
        return await db.TenantConfigAuditLogs.AsNoTracking()
            .Where(l => l.EntityName == "company_ot_mandate_rule" && l.TargetEntityId == company)
            .OrderBy(l => l.ChangedAt)
            .ToListAsync(Ct);
    }

    protected sealed record CompanyRuleSnapshot(bool Exists, string? AssignmentMode, long RowVersion);

    private async Task SeedAsync()
    {
        await using var db = CreateDbContext();

        db.TransitOffices.Add(new TransitOffice
        {
            Id = Office,
            Code = $"R{Guid.NewGuid():N}"[..10],
            Name = "OT F5 reglas por compañía",
            DepartmentCode = "99",
            CityCode = "99999",
            IsActive = true,
        });
        db.Tenants.AddRange(
            NewTenant(SuperAdminTenantId, "Empresa del SuperAdmin F5", "RENTING"),
            NewTenant(OtTenantId, "Tenant OT F5", "RENTING"),
            NewTenant(Company, "Compañía C F5", "RENTING"),
            NewTenant(CompanyWithoutGrant, "Compañía D sin grant F5", "RENTING"));
        await db.SaveChangesAsync(Ct);

        db.Users.AddRange(
            NewUser(SuperAdminUserId, SuperAdminTenantId, "superadmin"),
            NewUser(OtUserId, OtTenantId, "otadmin"),
            NewUser(CompanyAdminUserId, Company, "companyadmin"));
        db.TransitOfficeProfiles.Add(new TransitOfficeProfile
        {
            Id = Guid.NewGuid(),
            TenantId = OtTenantId,
            TransitOfficeId = Office,
            OperationMode = "dashboard",
            QuipuxReadOnly = false,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        db.TenantTransitOfficeGrants.Add(new TenantTransitOfficeGrant
        {
            Id = Guid.NewGuid(),
            TenantId = Company,
            TransitOfficeId = Office,
            IsEnabled = true,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync(Ct);
    }

    private static Tenant NewTenant(Guid id, string legalName, string type) => new()
    {
        Id = id,
        Code = $"F5-{Guid.NewGuid():N}"[..20],
        LegalName = legalName,
        TaxId = TestNit.Unique(),
        TenantType = type,
        IsActive = true,
        CreatedAt = DateTimeOffset.UtcNow,
    };

    private static User NewUser(Guid id, Guid tenant, string prefix) => new()
    {
        Id = id,
        Email = $"{prefix}-{id:N}@flit.local",
        DisplayName = $"{prefix} de prueba F5",
        Status = "active",
        HomeTenantId = tenant,
        CreatedAt = DateTimeOffset.UtcNow,
    };

    private static string MintToken(string role, Guid tenantId, Guid userId, string? entityType = null)
    {
        var claims = new List<Claim>
        {
            new("sub", userId.ToString()),
            new("role", role),
            new("tenant_id", tenantId.ToString()),
        };
        if (entityType is not null)
            claims.Add(new Claim(AdminAuthorization.EntityTypeClaimType, entityType));

        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = "https://api.flit.co",
            Audience = "flit-api",
            Subject = new ClaimsIdentity(claims),
            Expires = DateTime.UtcNow.AddHours(1),
            SigningCredentials = new SigningCredentials(DummyKey, SecurityAlgorithms.HmacSha256),
        });
    }

    /// <summary>Limpieza de la semilla; las subclases que siembran más datos la extienden.</summary>
    public virtual void Dispose()
    {
        using var db = CreateDbContext();
        var users = new[] { SuperAdminUserId, OtUserId, CompanyAdminUserId };
        var tenants = new[] { SuperAdminTenantId, OtTenantId, Company, CompanyWithoutGrant };

        db.TenantConfigAuditLogs.Where(l => l.ChangedBy != null && users.Contains(l.ChangedBy.Value)).ExecuteDelete();
        db.CompanyOtMandateRules.Where(r => r.TransitOfficeId == Office).ExecuteDelete();
        db.TenantTransitOfficeGrants.Where(g => tenants.Contains(g.TenantId)).ExecuteDelete();
        db.TransitOfficeProfiles.Where(p => p.TenantId == OtTenantId).ExecuteDelete();
        db.Users.Where(u => users.Contains(u.Id)).ExecuteDelete();
        db.Tenants.Where(t => tenants.Contains(t.Id)).ExecuteDelete();
        db.TransitOffices.Where(o => o.Id == Office).ExecuteDelete();
        GC.SuppressFinalize(this);
    }
}
