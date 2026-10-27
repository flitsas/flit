using System.Reflection;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Security;
using Flit.Infrastructure.Security;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Flit.Infrastructure.Tests.Security;

/// <summary>
/// HU #13640 (Feature #13100, Épica #12753) — el seeder deja de re-otorgar permisos a roles de sistema que ya
/// existen: el default de un rol se siembra solo cuando el permiso se CREA en el catálogo; si el Super Admin lo
/// retiró, un reinicio (segunda ejecución del seeder) no lo devuelve. SuperAdmin se re-concilia siempre.
/// </summary>
/// <remarks>
/// Se ejercitan por reflexión los pasos del catálogo (<c>SeedAsync</c> ejecuta SQL crudo que InMemory no soporta).
/// "Reiniciar la API" = volver a invocar los pasos sobre la misma base con un contexto nuevo.
/// </remarks>
public sealed class SeederNoRegrantTests
{
    private static readonly string[] CatalogSteps =
    [
        "SeedResetPasswordPermissionsAsync",
        "SeedHistorialPlacaPermissionsAsync",
        "SeedGeneracionDocumentalPermissionsAsync",
        "SeedIctLogsPermissionsAsync",
        "SeedIctTrazabilidadYReportesPermissionsAsync",
        "SeedConsolidadoMasivoPermissionsAsync",
        "SeedBannersPermissionsAsync",
    ];

    [Fact]
    public async Task AC1_AdminCompanySinResetPassword_ElReinicioNoSeLoDevuelve()
    {
        var ct = TestContext.Current.CancellationToken;
        var dbName = await FirstRunAsync(ct);

        await using (var check = NewContext(dbName))
            (await GrantedAsync(check, "security.users.reset_password", ct)).Should().BeEquivalentTo(["SuperAdmin", "AdminCompany"]);

        await RevokeAsync(dbName, "security.users.reset_password", "AdminCompany", ct);
        await RunAsync(dbName, ct);

        await using var after = NewContext(dbName);
        (await GrantedAsync(after, "security.users.reset_password", ct)).Should().BeEquivalentTo(["SuperAdmin"]);
    }

    [Fact]
    public async Task AC2_HistorialPlacaYGeneracionDocumentalRetirados_ElReinicioNoLosDevuelve()
    {
        var ct = TestContext.Current.CancellationToken;
        var dbName = await FirstRunAsync(ct);

        await RevokeAsync(dbName, "historial-placa.read", "admin_tramites", ct);
        await RevokeAsync(dbName, "generacion-documental.read", "admin_tramites", ct);
        await RunAsync(dbName, ct);

        await using var after = NewContext(dbName);
        (await GrantedAsync(after, "historial-placa.read", ct)).Should().BeEquivalentTo(["SuperAdmin", "Radicador"]);
        (await GrantedAsync(after, "generacion-documental.read", ct)).Should().BeEquivalentTo(["SuperAdmin"]);
        // Lo que no se retiró se conserva y no se duplica.
        (await GrantedAsync(after, "generacion-documental.generate", ct)).Should().BeEquivalentTo(["SuperAdmin", "admin_tramites"]);
        (await GrantedAsync(after, "ict.trazabilidad.read", ct)).Should().BeEquivalentTo(["SuperAdmin", "admin_tramites"]);
        (await GrantedAsync(after, "consolidado-masivo.download", ct))
            .Should().BeEquivalentTo(["SuperAdmin", "admin_tramites", "Radicador", "ot_admin"]);
    }

    [Fact]
    public async Task AC2b_OtrosRetirosDeRolesDeSistema_TampocoVuelven()
    {
        var ct = TestContext.Current.CancellationToken;
        var dbName = await FirstRunAsync(ct);

        await RevokeAsync(dbName, "ict.reportes.read", "admin_tramites", ct);
        await RevokeAsync(dbName, "consolidado-masivo.download", "ot_admin", ct);
        await RunAsync(dbName, ct);

        await using var after = NewContext(dbName);
        (await GrantedAsync(after, "ict.reportes.read", ct)).Should().BeEquivalentTo(["SuperAdmin"]);
        (await GrantedAsync(after, "consolidado-masivo.download", ct))
            .Should().BeEquivalentTo(["SuperAdmin", "admin_tramites", "Radicador"]);
    }

    [Fact]
    public async Task AC3_BaseSinElRolDeSistema_ElRolSeCreaConTodosSusGrants()
    {
        var ct = TestContext.Current.CancellationToken;
        var dbName = Guid.NewGuid().ToString();
        await using (var seed = NewContext(dbName))
        {
            AddRole(seed, "SuperAdmin");
            AddRole(seed, "AdminCompany");
            AddRole(seed, "admin_tramites");
            AddRole(seed, "Radicador");
            AddRole(seed, "ot_admin");
            seed.SecurityModules.Add(new SecurityModule
            {
                Id = Guid.CreateVersion7(),
                Code = "usuarios",
                Name = "Usuarios y Permisos",
                SortOrder = 5,
                IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow,
            });
            await seed.SaveChangesAsync(ct);
        }

        // Base virgen: permisos inexistentes -> se crean y los roles reciben sus grants por defecto.
        await RunAsync(dbName, ct);
        await RunAsync(dbName, ct); // un segundo arranque no cambia nada

        await using var check = NewContext(dbName);
        (await GrantedAsync(check, "security.users.reset_password", ct)).Should().BeEquivalentTo(["SuperAdmin", "AdminCompany"]);
        (await GrantedAsync(check, "historial-placa.read", ct)).Should().BeEquivalentTo(["SuperAdmin", "admin_tramites", "Radicador"]);
        (await GrantedAsync(check, "generacion-documental.read", ct)).Should().BeEquivalentTo(["SuperAdmin", "admin_tramites"]);
        (await GrantedAsync(check, "generacion-documental.generate", ct)).Should().BeEquivalentTo(["SuperAdmin", "admin_tramites"]);
        (await GrantedAsync(check, "ict.trazabilidad.read", ct)).Should().BeEquivalentTo(["SuperAdmin", "admin_tramites"]);
        (await GrantedAsync(check, "consolidado-masivo.download", ct))
            .Should().BeEquivalentTo(["SuperAdmin", "admin_tramites", "Radicador", "ot_admin"]);
    }

    [Fact]
    public async Task AC4_BannersManageOtorgadoAAdminCompany_ElReinicioLoDejaSoloEnSuperAdmin()
    {
        var ct = TestContext.Current.CancellationToken;
        var dbName = await FirstRunAsync(ct);

        await using (var db = NewContext(dbName))
        {
            var adminCompany = await db.Roles.SingleAsync(r => r.Code == "AdminCompany", ct);
            var action = await db.RbacActions.SingleAsync(a => a.Slug == "banners.manage", ct);
            db.RoleGrants.Add(new RoleGrant
            {
                Id = Guid.CreateVersion7(),
                RoleId = adminCompany.Id,
                PermissionId = action.Id,
                CreatedAt = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync(ct);
        }

        await RunAsync(dbName, ct);

        await using var after = NewContext(dbName);
        (await GrantedAsync(after, "banners.manage", ct)).Should().BeEquivalentTo(["SuperAdmin"]);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────────────

    private static async Task<string> FirstRunAsync(CancellationToken ct)
    {
        var dbName = Guid.NewGuid().ToString();
        await using (var seed = NewContext(dbName))
        {
            AddRole(seed, "SuperAdmin");
            AddRole(seed, "AdminCompany");
            AddRole(seed, "admin_tramites");
            AddRole(seed, "Radicador");
            AddRole(seed, "ot_admin");
            seed.SecurityModules.Add(new SecurityModule
            {
                Id = Guid.CreateVersion7(),
                Code = "usuarios",
                Name = "Usuarios y Permisos",
                SortOrder = 5,
                IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow,
            });
            await seed.SaveChangesAsync(ct);
        }

        await RunAsync(dbName, ct);
        return dbName;
    }

    /// <summary>Un "reinicio": cada paso del catálogo sobre un contexto nuevo de la misma base.</summary>
    private static async Task RunAsync(string dbName, CancellationToken ct)
    {
        foreach (var step in CatalogSteps)
        {
            await using var db = NewContext(dbName);
            var method = typeof(DevelopmentAuthSeeder).GetMethod(
                step, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static);
            method.Should().NotBeNull($"el paso {step} debe existir en DevelopmentAuthSeeder");
            await (Task)method!.Invoke(null, [db, ct])!;
        }
    }

    private static async Task RevokeAsync(string dbName, string slug, string roleCode, CancellationToken ct)
    {
        await using var db = NewContext(dbName);
        var grants = await (
            from g in db.RoleGrants
            join a in db.RbacActions on g.PermissionId equals a.Id
            join r in db.Roles on g.RoleId equals r.Id
            where a.Slug == slug && r.Code == roleCode
            select g).ToListAsync(ct);
        grants.Should().NotBeEmpty($"{roleCode} debía tener {slug} tras el primer arranque");
        db.RoleGrants.RemoveRange(grants);
        await db.SaveChangesAsync(ct);
    }

    private static async Task<List<string>> GrantedAsync(FlitDbContext db, string slug, CancellationToken ct) =>
        await (
            from g in db.RoleGrants
            join a in db.RbacActions on g.PermissionId equals a.Id
            join r in db.Roles on g.RoleId equals r.Id
            where a.Slug == slug
            select r.Code).ToListAsync(ct);

    private static FlitDbContext NewContext(string dbName) =>
        new(new DbContextOptionsBuilder<FlitDbContext>().UseInMemoryDatabase(dbName).Options);

    private static void AddRole(FlitDbContext db, string code) =>
        db.Roles.Add(new Role
        {
            Id = Guid.CreateVersion7(),
            Code = code,
            Name = code,
            TargetEntityType = "COMPANY",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        });
}
