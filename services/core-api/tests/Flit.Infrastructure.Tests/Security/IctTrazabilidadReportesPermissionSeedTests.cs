using System.Reflection;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Security;
using Flit.Infrastructure.Security;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Flit.Infrastructure.Tests.Security;

/// <summary>
/// Bug #13445, punto 4 (decisiones D10/D11) — permisos <c>ict.trazabilidad.read</c> e
/// <c>ict.reportes.read</c> en el módulo <c>ict-logs</c>, concedidos a SuperAdmin y a <c>admin_tramites</c>.
/// </summary>
/// <remarks>
/// Uso de ejemplo (producción): <c>DevelopmentAuthSeeder.SeedAsync(db, hasher, settings, ct)</c> corre
/// <c>SeedIctLogsPermissionsAsync</c> y luego <c>SeedIctTrazabilidadYReportesPermissionsAsync</c>. Aquí se
/// invocan esos dos pasos privados por reflexión sobre InMemory (mismo criterio que
/// <see cref="HistorialPlacaPermissionSeedTests"/>): <c>SeedAsync</c> ejecuta SQL crudo que InMemory no soporta.
/// </remarks>
public sealed class IctTrazabilidadReportesPermissionSeedTests
{
    private const string Trazabilidad = "ict.trazabilidad.read";
    private const string Reportes = "ict.reportes.read";

    [Fact]
    public async Task Seed_CreaLosDosPermisosEnIctLogsYLosConcedeASuperAdminYAdminTramites()
    {
        var ct = TestContext.Current.CancellationToken;
        var dbName = await NewDbWithRolesAsync(ct, "SuperAdmin", "admin_tramites", "AdminCompany", "Radicador");

        await using (var db = NewContext(dbName))
        {
            await InvokeAsync(db, "SeedIctLogsPermissionsAsync", ct);
            await InvokeAsync(db, "SeedIctTrazabilidadYReportesPermissionsAsync", ct);
        }

        await using var check = NewContext(dbName);
        var module = await check.SecurityModules.SingleAsync(m => m.Code == "ict-logs", ct);
        foreach (var slug in new[] { Trazabilidad, Reportes })
        {
            var action = await check.RbacActions.SingleAsync(a => a.Slug == slug, ct);
            action.ModuleId.Should().Be(module.Id, "el permiso cuelga de ict-logs (producto tramites)");
            action.IsActive.Should().BeTrue();
            (await GrantedRoleCodesAsync(check, action.Id, ct))
                .Should().BeEquivalentTo(["SuperAdmin", "admin_tramites"],
                    "AdminCompany es de plataforma y el trigger same_product lo rechazaría; Radicador no entra en D10");
        }
    }

    [Fact]
    public async Task Seed_NoConcedeIctLogsReadNiIctPiiRevealAAdminTramites()
    {
        var ct = TestContext.Current.CancellationToken;
        var dbName = await NewDbWithRolesAsync(ct, "SuperAdmin", "admin_tramites");

        await using (var db = NewContext(dbName))
        {
            await InvokeAsync(db, "SeedIctLogsPermissionsAsync", ct);
            await InvokeAsync(db, "SeedIctPiiRevealPermissionAsync", ct);
            await InvokeAsync(db, "SeedIctTrazabilidadYReportesPermissionsAsync", ct);
        }

        await using var check = NewContext(dbName);
        var adminTramites = await check.Roles.SingleAsync(r => r.Code == "admin_tramites", ct);
        var slugs = await (
            from g in check.RoleGrants
            join a in check.RbacActions on g.PermissionId equals a.Id
            where g.RoleId == adminTramites.Id
            select a.Slug).ToListAsync(ct);

        slugs.Should().BeEquivalentTo([Trazabilidad, Reportes],
            "Logs ICT es solo SuperAdmin (D10) y la PII no se revela por defecto (D11)");
    }

    [Fact]
    public async Task Seed_EjecutadoDosVecesSobreBaseYaSembrada_NoDuplicaNada()
    {
        var ct = TestContext.Current.CancellationToken;
        var dbName = await NewDbWithRolesAsync(ct, "SuperAdmin", "admin_tramites");

        await using (var first = NewContext(dbName))
        {
            await InvokeAsync(first, "SeedIctLogsPermissionsAsync", ct);
            await InvokeAsync(first, "SeedIctTrazabilidadYReportesPermissionsAsync", ct);
        }

        await using (var second = NewContext(dbName))
        {
            await InvokeAsync(second, "SeedIctLogsPermissionsAsync", ct);
            await InvokeAsync(second, "SeedIctTrazabilidadYReportesPermissionsAsync", ct);
        }

        await using var check = NewContext(dbName);
        foreach (var slug in new[] { Trazabilidad, Reportes })
        {
            var actions = await check.RbacActions.Where(a => a.Slug == slug).ToListAsync(ct);
            actions.Should().HaveCount(1);
            (await check.RoleGrants.CountAsync(g => g.PermissionId == actions[0].Id, ct)).Should().Be(2);
        }
    }

    [Fact]
    public async Task Seed_SinModuloIctLogs_NoCreaPermisosHuerfanos()
    {
        var ct = TestContext.Current.CancellationToken;
        var dbName = await NewDbWithRolesAsync(ct, "SuperAdmin", "admin_tramites");

        await using (var db = NewContext(dbName))
        {
            await InvokeAsync(db, "SeedIctTrazabilidadYReportesPermissionsAsync", ct);
        }

        await using var check = NewContext(dbName);
        (await check.RbacActions.CountAsync(a => a.Slug == Trazabilidad || a.Slug == Reportes, ct)).Should().Be(0);
    }

    [Fact]
    public async Task Seed_ConAdminTramitesBorrado_SoloConcedeASuperAdmin()
    {
        var ct = TestContext.Current.CancellationToken;
        var dbName = Guid.NewGuid().ToString();
        await using (var seed = NewContext(dbName))
        {
            AddRole(seed, "SuperAdmin");
            AddRole(seed, "admin_tramites", deleted: true);
            await seed.SaveChangesAsync(ct);
        }

        await using (var db = NewContext(dbName))
        {
            await InvokeAsync(db, "SeedIctLogsPermissionsAsync", ct);
            await InvokeAsync(db, "SeedIctTrazabilidadYReportesPermissionsAsync", ct);
        }

        await using var check = NewContext(dbName);
        var action = await check.RbacActions.SingleAsync(a => a.Slug == Reportes, ct);
        (await GrantedRoleCodesAsync(check, action.Id, ct)).Should().BeEquivalentTo(["SuperAdmin"]);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────────────

    private static FlitDbContext NewContext(string dbName) =>
        new(new DbContextOptionsBuilder<FlitDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options);

    private static async Task<string> NewDbWithRolesAsync(CancellationToken ct, params string[] roleCodes)
    {
        var dbName = Guid.NewGuid().ToString();
        await using var seed = NewContext(dbName);
        foreach (var code in roleCodes)
        {
            AddRole(seed, code);
        }

        await seed.SaveChangesAsync(ct);
        return dbName;
    }

    private static void AddRole(FlitDbContext db, string code, bool deleted = false) =>
        db.Roles.Add(new Role
        {
            Id = Guid.CreateVersion7(),
            Code = code,
            Name = code,
            TargetEntityType = "COMPANY",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
            DeletedAt = deleted ? DateTimeOffset.UtcNow : null,
        });

    private static async Task<List<string>> GrantedRoleCodesAsync(FlitDbContext db, Guid permissionId, CancellationToken ct)
    {
        var roleIds = await db.RoleGrants
            .Where(g => g.PermissionId == permissionId)
            .Select(g => g.RoleId)
            .ToListAsync(ct);

        return await db.Roles
            .Where(r => roleIds.Contains(r.Id))
            .Select(r => r.Code)
            .ToListAsync(ct);
    }

    /// <summary>Invoca un paso privado del seeder (la superficie pública es solo <c>SeedAsync</c>).</summary>
    private static async Task InvokeAsync(FlitDbContext db, string methodName, CancellationToken ct)
    {
        var method = typeof(DevelopmentAuthSeeder).GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Static);

        method.Should().NotBeNull($"el paso {methodName} debe existir en DevelopmentAuthSeeder");

        await (Task)method!.Invoke(null, [db, ct])!;
    }
}
