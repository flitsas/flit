using Flit.Admin.Domain.Companies.Branding;
using Flit.Admin.Domain.Companies.Create;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Admin;
using Flit.Infrastructure.Persistence.Entities.Identity;
using Flit.Infrastructure.Persistence.Entities.Security;
using Flit.Infrastructure.Persistence.Repositories;
using Flit.Integration.Tests.Postgres;
using Flit.Modules.Security.Domain.Auth;
using Flit.Queries.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Flit.Integration.Tests.MarcaBlanca;

/// <summary>
/// HU #12429 — escenario canónico de la suite de paridad y anti-enumeración de Marca Blanca, sobre
/// la misma base efímera del arnés (<see cref="PostgresDatabaseFixture"/>, patrón
/// <c>Tenancy/HierarchyScenario</c> y <c>Network/NetworkLoginScopeTests</c>).
/// <list type="bullet">
///   <item><b>Red A</b>: <see cref="HeadA"/> (MARCA_BLANCA, cabeza de grupo, dominio ACTIVO
///   <see cref="HostA"/>, marca publicada "Red Alfa") y su hija <see cref="ChildA"/>.</item>
///   <item><b>Red B</b>: igual forma, marca "Red Beta" y dominio <see cref="HostB"/> — para AC4
///   (aislamiento) y AC2 (usuario de otra red).</item>
///   <item><b>Concesión</b>: <see cref="ConcesionHead"/> (cabeza de grupo, pero NUNCA red MARCA_BLANCA)
///   con su hija <see cref="ConcesionChild"/> — AC1/AC6 (paridad FLIT) y el endurecimiento del hecho 88
///   (policy de cabeza).</item>
///   <item><b>Sin red</b>: <see cref="Lone"/>, compañía aislada — AC1/AC6.</item>
/// </list>
/// Cada tenant con usuario tiene un usuario ACTIVO con la MISMA contraseña conocida
/// (<see cref="Password"/>) y CERO asignaciones de rol (el login no las exige: solo bloquea si
/// tuvo roles y todos quedaron inactivos, HU #10507 AC3) — la siembra queda mínima a propósito.
/// </summary>
internal static class MarcaBlancaScenario
{
    public const string Password = "MarcaBlanca1!";

    public const string HostA = "app.red-alfa-it.example";
    public const string HostB = "app.red-beta-it.example";

    public const string PlatformNameA = "Red Alfa";
    public const string PlatformNameB = "Red Beta";

    public static readonly BrandColors ColorsA = new("#101010", "#AA00FF", "#FFFFFF");
    public static readonly BrandColors ColorsB = new("#202020", "#00AAFF", "#000000");

    public static readonly Guid HeadA = new("a1000000-1111-4111-8111-111111111111");
    public static readonly Guid ChildA = new("a1000000-2222-4222-8222-222222222222");
    public static readonly Guid HeadB = new("a2000000-1111-4111-8111-111111111111");
    public static readonly Guid ChildB = new("a2000000-2222-4222-8222-222222222222");
    public static readonly Guid ConcesionHead = new("a3000000-1111-4111-8111-111111111111");
    public static readonly Guid ConcesionChild = new("a3000000-2222-4222-8222-222222222222");
    public static readonly Guid Lone = new("a4000000-1111-4111-8111-111111111111");

    public static readonly Guid UserHeadA = new("a1000000-9999-4999-8999-000000000001");
    public static readonly Guid UserChildA = new("a1000000-9999-4999-8999-000000000002");
    public static readonly Guid UserHeadB = new("a2000000-9999-4999-8999-000000000001");
    public static readonly Guid UserConcesionChild = new("a3000000-9999-4999-8999-000000000002");
    public static readonly Guid UserLone = new("a4000000-9999-4999-8999-000000000001");

    public static string EmailOf(Guid userId) => $"mb-{userId:N}@flit.test";

    /// <summary>Siembra tenants + dominios activos + marcas publicadas + usuarios con contraseña conocida.</summary>
    public static async Task SeedAsync(PostgresDatabaseFixture fixture, IPasswordHasher hasher)
    {
        await using (var ctx = fixture.CreateDbContext())
        {
            ctx.Tenants.Add(NewTenant(HeadA, "IT-MB-HEAD-A", isGroupParent: true, parentId: null, HeadTenantTypes.MarcaBlanca));
            ctx.Tenants.Add(NewTenant(HeadB, "IT-MB-HEAD-B", isGroupParent: true, parentId: null, HeadTenantTypes.MarcaBlanca));
            ctx.Tenants.Add(NewTenant(ConcesionHead, "IT-MB-CN-HEAD", isGroupParent: true, parentId: null, HeadTenantTypes.Concesion));
            ctx.Tenants.Add(NewTenant(Lone, "IT-MB-LONE", isGroupParent: false, parentId: null));
            await ctx.SaveChangesAsync();
        }

        await using (var ctx = fixture.CreateDbContext())
        {
            ctx.Tenants.Add(NewTenant(ChildA, "IT-MB-CHILD-A", isGroupParent: false, parentId: HeadA));
            ctx.Tenants.Add(NewTenant(ChildB, "IT-MB-CHILD-B", isGroupParent: false, parentId: HeadB));
            ctx.Tenants.Add(NewTenant(ConcesionChild, "IT-MB-CN-CHILD", isGroupParent: false, parentId: ConcesionHead));
            await ctx.SaveChangesAsync();
        }

        await using (var ctx = fixture.CreateDbContext())
        {
            ctx.TenantDomains.AddRange(
                ActiveDomain(HeadA, HostA, "tok-mb-red-alfa-0000000001"),
                ActiveDomain(HeadB, HostB, "tok-mb-red-beta-0000000002"));
            await ctx.SaveChangesAsync();
        }

        // Los usuarios se siembran ANTES de publicar la marca: tenant_config_audit_logs.changed_by
        // tiene FK a identity.users (tenant_config_audit_logs_changed_by_fkey) — el autor de la
        // publicación debe ser un usuario real, cada cabeza publica con SU propio usuario.
        await using (var ctx = fixture.CreateDbContext())
        {
            var passwordHash = hasher.Hash(Password);
            ctx.Users.AddRange(
                NewUser(UserHeadA, HeadA),
                NewUser(UserChildA, ChildA),
                NewUser(UserHeadB, HeadB),
                NewUser(UserConcesionChild, ConcesionChild),
                NewUser(UserLone, Lone));
            await ctx.SaveChangesAsync();

            ctx.UserCredentials.AddRange(
                NewCredential(UserHeadA, passwordHash),
                NewCredential(UserChildA, passwordHash),
                NewCredential(UserHeadB, passwordHash),
                NewCredential(UserConcesionChild, passwordHash),
                NewCredential(UserLone, passwordHash));
            await ctx.SaveChangesAsync();
        }

        await using (var ctx = fixture.CreateDbContext())
        {
            var repo = new TenantBrandingRepository(ctx);
            await repo.UpsertDraftAsync(HeadA, new BrandingDraft(PlatformNameA, ColorsA, null), changedBy: UserHeadA);
            await repo.PublishAsync(HeadA, changedBy: UserHeadA);
            await repo.UpsertDraftAsync(HeadB, new BrandingDraft(PlatformNameB, ColorsB, null), changedBy: UserHeadB);
            await repo.PublishAsync(HeadB, changedBy: UserHeadB);
        }
    }

    /// <summary>Borra todo lo sembrado (housekeeping en BD compartida entre suites de la misma clase de pruebas).</summary>
    public static async Task CleanupAsync(PostgresDatabaseFixture fixture)
    {
        var tenants = new[] { HeadA, ChildA, HeadB, ChildB, ConcesionHead, ConcesionChild, Lone };
        var users = new[] { UserHeadA, UserChildA, UserHeadB, UserConcesionChild, UserLone };

        await using var ctx = fixture.CreateDbContext();
        await ctx.UserCredentials.Where(c => users.Contains(c.UserId)).ExecuteDeleteAsync();
        await ctx.TenantConfigAuditLogs.Where(a => tenants.Contains(a.TenantId!.Value) || (a.ChangedBy != null && users.Contains(a.ChangedBy.Value))).ExecuteDeleteAsync();
        await ctx.Users.Where(u => users.Contains(u.Id)).ExecuteDeleteAsync();
        await ctx.TenantBrandings.Where(b => tenants.Contains(b.TenantId)).ExecuteDeleteAsync();
        await ctx.TenantDomains.Where(d => tenants.Contains(d.TenantId)).ExecuteDeleteAsync();
        await ctx.Tenants.Where(t => tenants.Contains(t.Id)).ExecuteDeleteAsync();
    }

    private static TenantDomainEntity ActiveDomain(Guid tenantId, string host, string token)
    {
        var now = DateTimeOffset.UtcNow;
        return new TenantDomainEntity
        {
            TenantId = tenantId,
            Host = host,
            VerificationToken = token,
            Status = TenantDomainStatuses.Active,
            VerifiedAt = now,
            ActivatedAt = now,
            CertificateIssuedAt = now,
            CertificateExpiresAt = now.AddDays(90),
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    private static User NewUser(Guid id, Guid tenantId) => new()
    {
        Id = id,
        Email = EmailOf(id),
        DisplayName = $"Usuario {id:N}",
        Status = "active",
        HomeTenantId = tenantId,
        CreatedAt = DateTimeOffset.UtcNow.AddDays(-30),
    };

    private static UserCredential NewCredential(Guid userId, string passwordHash) => new()
    {
        Id = Guid.NewGuid(),
        UserId = userId,
        PasswordHash = passwordHash,
        CreatedAt = DateTimeOffset.UtcNow,
    };

    /// <summary><see cref="TenantSeed.New"/> con NIT único (los ids del escenario comparten prefijo).</summary>
    private static Flit.Infrastructure.Persistence.Entities.Identity.Tenant NewTenant(
        Guid id, string code, bool isGroupParent, Guid? parentId, string? tenantType = null)
    {
        var tenant = TenantSeed.New(id, code, isGroupParent, parentId, tenantType);
        tenant.TaxId = $"8{id:N}"[..15];
        return tenant;
    }
}
