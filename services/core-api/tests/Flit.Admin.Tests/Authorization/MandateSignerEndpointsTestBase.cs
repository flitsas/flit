using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Flit.Admin.Domain.Companies.MandateSigners;
using Flit.Api.Authorization;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Admin;
using Flit.Infrastructure.Persistence.Entities.Catalogs;
using Flit.Infrastructure.Persistence.Entities.Identity;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Xunit;


namespace Flit.Admin.Tests.Authorization;

/// <summary>
/// Base de las pruebas de endpoints de mandatarios sobre PostgreSQL real (HU #13134–#13138, Feature #13115): siembra
/// organismos, compañías (propia, ajena, cabeza de red e hija), usuarios por rol y mandatarios con su origen, y
/// limpia al terminar.
/// </summary>
public abstract class MandateSignerEndpointsTestBase
    : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private static readonly SymmetricSecurityKey DummyKey = new(Encoding.UTF8.GetBytes(new string('k', 64)));

    protected readonly WebApplicationFactory<Program> _factory;
    protected readonly HttpClient _client;

    protected readonly Guid _officeA = Guid.NewGuid();
    protected readonly Guid _officeB = Guid.NewGuid();
    protected readonly Guid _otTenant = Guid.NewGuid();
    protected readonly Guid _otAdminUser = Guid.NewGuid();
    protected readonly Guid _superAdminTenant = Guid.NewGuid();
    protected readonly Guid _superAdminUser = Guid.NewGuid();
    protected readonly Guid _companyA = Guid.NewGuid();
    protected readonly Guid _companyAUser = Guid.NewGuid();
    protected readonly Guid _companyB = Guid.NewGuid();
    protected readonly Guid _companyBUser = Guid.NewGuid();
    protected readonly Guid _head = Guid.NewGuid();
    protected readonly Guid _headUser = Guid.NewGuid();
    protected readonly Guid _child = Guid.NewGuid();
    protected readonly Guid _gestorUser = Guid.NewGuid();
    protected readonly List<Guid> _signerIds = [];

    protected MandateSignerEndpointsTestBase(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
        SeedAsync().GetAwaiter().GetResult();
    }

    protected static CancellationToken Ct => TestContext.Current.CancellationToken;

    protected string Hub(Guid signer, string suffix = "") =>
        $"/api/v1/admin/transit-offices/{_officeA}/mandate-signers/{signer}{suffix}";

    protected string Company(Guid tenant, Guid signer, string suffix = "") =>
        $"/api/v1/admin/companies/{tenant}/mandate-signers/{signer}{suffix}";

    protected string Child(Guid signer, string suffix = "") =>
        $"/api/v1/admin/companies/{_head}/children/{_child}/mandate-signers/{signer}{suffix}";

    // ── Infraestructura ─────────────────────────────────────────────────────────────────────────

    protected async Task<JsonElement> ListAsync(string url)
    {
        var response = await _client.GetAsync(url, Ct);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        return (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("data");
    }

    protected static (string Origin, bool Edit, bool Delete) Flags(JsonElement data, Guid signerId)
    {
        var row = data.EnumerateArray().Single(e => e.GetProperty("id").GetGuid() == signerId);
        return (
            row.GetProperty("origin").GetString()!,
            row.GetProperty("puedeEditar").GetBoolean(),
            row.GetProperty("puedeEliminar").GetBoolean());
    }

    protected async Task AssertSignerUntouchedAsync(Guid signerId)
    {
        await using var db = NewDb();
        var signer = await db.MandateSigners.AsNoTracking().SingleAsync(s => s.Id == signerId, Ct);
        signer.IsActive.Should().BeTrue();
        signer.DeletedAt.Should().BeNull();
        (await db.MandateSignerCompanies.AsNoTracking().Where(c => c.MandateSignerId == signerId).ToListAsync(Ct))
            .Should().OnlyContain(c => c.IsActive);
    }

    protected void Authenticate(string role, Guid tenant, Guid user, string? entityType = null) =>
        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", MintToken(role, tenant, user, entityType));

    protected void AuthenticateOt(string role = "ot_admin") =>
        Authenticate(role, _otTenant, _otAdminUser, AdminAuthorization.TransitOfficeEntityType);

    protected void AuthenticateSuperAdmin() => Authenticate("SuperAdmin", _superAdminTenant, _superAdminUser);

    protected void AuthenticateCompanyA() => Authenticate("AdminCompany", _companyA, _companyAUser);

    protected void AuthenticateCompanyB() => Authenticate("AdminCompany", _companyB, _companyBUser);

    protected void AuthenticateHead() => Authenticate("AdminCompany", _head, _headUser);

    protected void AuthenticateGestor() => Authenticate("gestor_tramites", _companyA, _gestorUser);

    protected FlitDbContext NewDb() =>
        _factory.Services.CreateScope().ServiceProvider.GetRequiredService<FlitDbContext>();

    protected async Task<Guid> SeedSignerAsync(string name, (Guid Company, string Scope)[] links)
    {
        await using var db = NewDb();
        var id = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        db.MandateSigners.Add(new MandateSigner
        {
            Id = id,
            TransitOfficeId = _officeA,
            FullName = name,
            DocumentType = "NIT",
            DocumentNumber = $"9{Random.Shared.Next(10000000, 99999999)}",
            IntegrityHash = new string('a', 64),
            RegisteredAt = now,
            IsActive = true,
            SignerModel = "juridica",
            ValidityKind = "fixed",
            CreatedAt = now,
        });
        db.MandateSignerTransitOffices.Add(new MandateSignerTransitOffice
        {
            Id = Guid.NewGuid(), MandateSignerId = id, TransitOfficeId = _officeA, IsActive = true, CreatedAt = now,
        });
        foreach (var (company, scope) in links)
        {
            db.MandateSignerCompanies.Add(new MandateSignerCompany
            {
                Id = Guid.NewGuid(), MandateSignerId = id, TransitOfficeId = _officeA, CompanyTenantId = company,
                IsActive = true, ConfiguredByScope = scope, CreatedAt = now,
            });
        }

        await db.SaveChangesAsync(Ct);
        _signerIds.Add(id);
        return id;
    }

    protected async Task SeedGrantAsync(Guid companyTenantId)
    {
        await using var db = NewDb();
        db.TenantTransitOfficeGrants.Add(new TenantTransitOfficeGrant
        {
            Id = Guid.NewGuid(), TenantId = companyTenantId, TransitOfficeId = _officeA, IsEnabled = true,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync(Ct);
    }

    /// <summary>Default de la compañía A y general del organismo A apuntando al mandatario.</summary>
    protected async Task SeedDefaultsAsync(Guid signerId)
    {
        await using var db = NewDb();
        db.CompanyOtMandateRules.Add(new CompanyOtMandateRuleEntity
        {
            Id = Guid.NewGuid(), CompanyTenantId = _companyA, TransitOfficeId = _officeA, AssignmentMode = "signer",
            DefaultMandateSignerId = signerId, CreatedAt = DateTimeOffset.UtcNow,
        });
        var config = await db.TransitOfficeMandateConfigs.FirstOrDefaultAsync(c => c.TransitOfficeId == _officeA, Ct);
        if (config is null)
        {
            db.TransitOfficeMandateConfigs.Add(new TransitOfficeMandateConfigEntity
            {
                Id = Guid.NewGuid(), TransitOfficeId = _officeA, AssignmentMode = "signer",
                DefaultMandateSignerId = signerId, CreatedAt = DateTimeOffset.UtcNow,
            });
        }
        else
        {
            config.DefaultMandateSignerId = signerId;
        }

        await db.SaveChangesAsync(Ct);
    }

    protected async Task SeedAsync()
    {
        await using var db = NewDb();

        db.TransitOffices.AddRange(NewOffice(_officeA, "OT A HU13134"), NewOffice(_officeB, "OT B HU13134"));
        db.Tenants.AddRange(
            NewTenant(_otTenant, "OT A tenant HU13134", "RENTING", false, null),
            NewTenant(_superAdminTenant, "Super admin HU13134", "RENTING", false, null),
            NewTenant(_companyA, "Compania A HU13134", "RENTING", false, null),
            NewTenant(_companyB, "Compania B HU13134", "RENTING", false, null),
            NewTenant(_head, "Cabeza HU13134", "CONCESION", true, null));
        await db.SaveChangesAsync(Ct);

        // Los triggers de identity.tenants exigen que la cabeza exista antes que la hija.
        db.Tenants.Add(NewTenant(_child, "Hija HU13134", "CONCESIONARIO", false, _head));
        db.TransitOfficeProfiles.Add(new TransitOfficeProfile
        {
            Id = Guid.NewGuid(), TenantId = _otTenant, TransitOfficeId = _officeA, OperationMode = "dashboard",
            QuipuxReadOnly = false, CreatedAt = DateTimeOffset.UtcNow,
        });
        foreach (var (user, tenant) in new[]
        {
            (_otAdminUser, _otTenant), (_superAdminUser, _superAdminTenant), (_companyAUser, _companyA),
            (_companyBUser, _companyB), (_headUser, _head), (_gestorUser, _companyA),
        })
        {
            db.Users.Add(new User
            {
                Id = user,
                Email = $"u-{user:N}@flit.local",
                DisplayName = "Usuario HU13134",
                Status = "active",
                HomeTenantId = tenant,
                CreatedAt = DateTimeOffset.UtcNow,
            });
        }

        await db.SaveChangesAsync(Ct);
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

    private static Tenant NewTenant(Guid id, string legalName, string type, bool isGroupParent, Guid? parent) => new()
    {
        Id = id,
        Code = $"H13134-{Guid.NewGuid():N}"[..20],
        LegalName = legalName,
        TaxId = TestNit.Unique(),
        TenantType = type,
        IsGroupParent = isGroupParent,
        ParentTenantId = parent,
        IsActive = true,
        CreatedAt = DateTimeOffset.UtcNow,
    };

    protected static string MintToken(string role, Guid tenantId, Guid userId, string? entityType)
    {
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

        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
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
        using var db = NewDb();
        var users = new[] { _otAdminUser, _superAdminUser, _companyAUser, _companyBUser, _headUser, _gestorUser };

        db.TenantConfigAuditLogs.Where(l => (l.ChangedBy.HasValue && users.Contains(l.ChangedBy.Value))
            || (l.TargetEntityId.HasValue && _signerIds.Contains(l.TargetEntityId.Value))).ExecuteDelete();
        db.CompanyOtMandateRules.Where(r => r.TransitOfficeId == _officeA || r.TransitOfficeId == _officeB).ExecuteDelete();
        db.TransitOfficeMandateConfigs.Where(c => c.TransitOfficeId == _officeA || c.TransitOfficeId == _officeB).ExecuteDelete();
        db.TenantTransitOfficeGrants.Where(g => g.TransitOfficeId == _officeA || g.TransitOfficeId == _officeB).ExecuteDelete();
        db.MandateSignerCompanies.Where(x => _signerIds.Contains(x.MandateSignerId)).ExecuteDelete();
        db.MandateSignerTransitOffices.Where(x => _signerIds.Contains(x.MandateSignerId)).ExecuteDelete();
        db.MandateSigners.Where(m => _signerIds.Contains(m.Id)).ExecuteDelete();
        db.TransitOfficeProfiles.Where(p => p.TenantId == _otTenant).ExecuteDelete();
        db.Users.Where(u => users.Contains(u.Id)).ExecuteDelete();
        db.Tenants.Where(t => t.Id == _child).ExecuteDelete();
        db.Tenants.Where(t => t.Id == _otTenant || t.Id == _superAdminTenant || t.Id == _companyA
            || t.Id == _companyB || t.Id == _head).ExecuteDelete();
        db.TransitOffices.Where(o => o.Id == _officeA || o.Id == _officeB).ExecuteDelete();
    }
}
