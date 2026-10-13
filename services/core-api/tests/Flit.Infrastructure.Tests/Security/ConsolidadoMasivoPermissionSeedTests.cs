using System.Reflection;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Security;
using Flit.Infrastructure.Security;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Flit.Infrastructure.Tests.Security;

/// <summary>
/// HU #13369 (épica #13216, Feature #13306) — registro del módulo <c>consolidado-masivo</c> y del permiso
/// <c>consolidado-masivo.download</c> en el catálogo RBAC. Calcado de <see cref="HistorialPlacaPermissionSeedTests"/>.
/// </summary>
/// <remarks>
/// <para>
/// Uso de ejemplo (producción): <c>DevelopmentAuthSeeder.SeedAsync(db, hasher, env, ct)</c> invoca, con
/// <c>Seed:RbacCatalog = true</c>, el seed de la descarga masiva. Aquí se ejercita SOLO ese método por reflexión,
/// porque <c>SeedAsync</c> ejecuta scripts SQL crudos que el proveedor InMemory no soporta.
/// </para>
/// <para>
/// Lo que se protege: grants directos a SuperAdmin, AdminCompany, Radicador y ot_admin (ADR-0070 A3.5 y A5.4),
/// idempotentes y sin duplicar; los demás roles (del OT o de la compañía) no lo reciben por defecto.
/// </para>
/// <para>
/// staging: sin multi-producto el Admin de Compañía es <c>AdminCompany</c> y recibe el grant directo; en develop es
/// <c>admin_tramites</c> (DDL 120, HU #12964) y AdminCompany lo obtiene por el espejo AdminCompany → admin_tramites,
/// con el disparador <c>security.tr_role_permissions_same_product</c>, que staging aún no tiene.
/// </para>
/// </remarks>
public sealed class ConsolidadoMasivoPermissionSeedTests
{
    private const string ModuleCode = "consolidado-masivo";
    private const string ActionSlug = "consolidado-masivo.download";

    // ── AC1 + AC4: módulo + permiso + grants a los cuatro roles ──────────────────────

    [Fact]
    public async Task Seed_CreaModuloPermisoYLoConcedeASuperAdminAdminCompanyRadicadorYOtAdmin()
    {
        var ct = TestContext.Current.CancellationToken;
        var dbName = Guid.NewGuid().ToString();

        await using (var seed = NewContext(dbName))
        {
            AddRole(seed, "SuperAdmin");
            AddRole(seed, "AdminCompany");
            AddRole(seed, "Radicador");
            AddRole(seed, "ot_admin", target: "TRANSIT_OFFICE");
            await seed.SaveChangesAsync(ct);
        }

        await using (var db = NewContext(dbName))
        {
            await InvokeSeedAsync(db, ct);
        }

        await using (var check = NewContext(dbName))
        {
            var module = await check.SecurityModules.SingleAsync(m => m.Code == ModuleCode, ct);
            module.Name.Should().Be("Descarga masiva de consolidados");
            module.IsActive.Should().BeTrue();
            module.SortOrder.Should().Be(13, "1..12 ya están ocupados por los módulos existentes");

            var action = await check.RbacActions.SingleAsync(a => a.Slug == ActionSlug, ct);
            action.ModuleId.Should().Be(module.Id, "el permiso debe colgar del módulo para ser administrable");
            action.HttpMethod.Should().Be("POST");
            action.RoutePattern.Should().Be("/api/v1/tramites/consolidados/lotes");
            action.IsActive.Should().BeTrue();

            var grantedRoleCodes = await GrantedRoleCodesAsync(check, action.Id, ct);
            grantedRoleCodes.Should().BeEquivalentTo(["SuperAdmin", "AdminCompany", "Radicador", "ot_admin"]);
        }
    }

    // ── AC2 (staging): AdminCompany recibe el grant directo, sin espejo ─────────────

    [Fact]
    public async Task Seed_ConAdminCompanyPresente_LeConcedeElGrantDirecto()
    {
        var ct = TestContext.Current.CancellationToken;
        var dbName = Guid.NewGuid().ToString();

        await using (var seed = NewContext(dbName))
        {
            AddRole(seed, "AdminCompany");
            await seed.SaveChangesAsync(ct);
        }

        await using (var db = NewContext(dbName))
        {
            await InvokeSeedAsync(db, ct);
        }

        await using (var check = NewContext(dbName))
        {
            var action = await check.RbacActions.SingleAsync(a => a.Slug == ActionSlug, ct);
            var grantedRoleCodes = await GrantedRoleCodesAsync(check, action.Id, ct);

            grantedRoleCodes.Should().BeEquivalentTo(["AdminCompany"],
                "staging: sin multi-producto el Admin de Compañía es AdminCompany y no hay espejo hacia admin_tramites");
        }
    }

    // ── AC3 + AC4 (staging): solo los cuatro roles objetivo reciben grant ───────────

    [Fact]
    public async Task Seed_SoloConcedeALosCuatroRolesObjetivo_NoAOtrosRolesDeLaCompania()
    {
        var ct = TestContext.Current.CancellationToken;
        var dbName = Guid.NewGuid().ToString();

        await using (var seed = NewContext(dbName))
        {
            AddRole(seed, "SuperAdmin");
            AddRole(seed, "AdminCompany");
            AddRole(seed, "Radicador");
            AddRole(seed, "ot_admin", target: "TRANSIT_OFFICE");
            AddRole(seed, "Auditor");
            AddRole(seed, "Consulta");
            AddRole(seed, "ot_operador", target: "TRANSIT_OFFICE");
            await seed.SaveChangesAsync(ct);
        }

        await using (var db = NewContext(dbName))
        {
            await InvokeSeedAsync(db, ct);
        }

        await using (var check = NewContext(dbName))
        {
            var action = await check.RbacActions.SingleAsync(a => a.Slug == ActionSlug, ct);
            var grantedRoleCodes = await GrantedRoleCodesAsync(check, action.Id, ct);
            grantedRoleCodes.Should().BeEquivalentTo(["SuperAdmin", "AdminCompany", "Radicador", "ot_admin"],
                "el resto de roles se conceden por RBAC, no por el seed");
        }
    }

    // ── AC4: los demás roles del OT no reciben el permiso por defecto ────────────────

    [Fact]
    public async Task Seed_OtrosRolesDelOt_NoRecibenElPermisoPorDefecto()
    {
        var ct = TestContext.Current.CancellationToken;
        var dbName = Guid.NewGuid().ToString();

        await using (var seed = NewContext(dbName))
        {
            AddRole(seed, "ot_admin", target: "TRANSIT_OFFICE");
            AddRole(seed, "ot_operador", target: "TRANSIT_OFFICE");
            AddRole(seed, "ot_consulta", target: "TRANSIT_OFFICE");
            await seed.SaveChangesAsync(ct);
        }

        await using (var db = NewContext(dbName))
        {
            await InvokeSeedAsync(db, ct);
        }

        await using (var check = NewContext(dbName))
        {
            var action = await check.RbacActions.SingleAsync(a => a.Slug == ActionSlug, ct);
            var grantedRoleCodes = await GrantedRoleCodesAsync(check, action.Id, ct);
            grantedRoleCodes.Should().BeEquivalentTo(["ot_admin"],
                "solo ot_admin recibe grant directo; el resto de roles OT se conceden por RBAC");
        }
    }

    // ── AC3: idempotencia ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Seed_EjecutadoDosVeces_NoDuplicaModuloPermisoNiGrants()
    {
        var ct = TestContext.Current.CancellationToken;
        var dbName = Guid.NewGuid().ToString();

        await using (var seed = NewContext(dbName))
        {
            AddRole(seed, "SuperAdmin");
            AddRole(seed, "AdminCompany");
            AddRole(seed, "Radicador");
            AddRole(seed, "ot_admin", target: "TRANSIT_OFFICE");
            await seed.SaveChangesAsync(ct);
        }

        await using (var first = NewContext(dbName))
        {
            await InvokeSeedAsync(first, ct);
        }

        await using (var second = NewContext(dbName))
        {
            await InvokeSeedAsync(second, ct);
        }

        await using (var check = NewContext(dbName))
        {
            (await check.SecurityModules.CountAsync(m => m.Code == ModuleCode, ct)).Should().Be(1);

            var actions = await check.RbacActions.Where(a => a.Slug == ActionSlug).ToListAsync(ct);
            actions.Should().HaveCount(1);

            (await check.RoleGrants.CountAsync(g => g.PermissionId == actions[0].Id, ct))
                .Should().Be(4, "un segundo pase no debe volver a conceder lo ya concedido");
        }
    }

    // ── Edge case: rol borrado o inexistente no recibe grant; catálogo queda poblado ──

    [Fact]
    public async Task Seed_ConRolBorradoOInexistente_NoLesConcedeElPermiso()
    {
        var ct = TestContext.Current.CancellationToken;
        var dbName = Guid.NewGuid().ToString();

        await using (var seed = NewContext(dbName))
        {
            AddRole(seed, "SuperAdmin");
            AddRole(seed, "AdminCompany", deleted: true);
            // "Radicador" y "ot_admin" no existen en este ambiente.
            AddRole(seed, "Auditor");
            await seed.SaveChangesAsync(ct);
        }

        await using (var db = NewContext(dbName))
        {
            await InvokeSeedAsync(db, ct);
        }

        await using (var check = NewContext(dbName))
        {
            var action = await check.RbacActions.SingleAsync(a => a.Slug == ActionSlug, ct);
            var grantedRoleCodes = await GrantedRoleCodesAsync(check, action.Id, ct);
            grantedRoleCodes.Should().BeEquivalentTo(["SuperAdmin"],
                "un rol borrado lógicamente o inexistente no recibe grant, y un rol ajeno tampoco");
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────────────

    private static FlitDbContext NewContext(string dbName) =>
        new(new DbContextOptionsBuilder<FlitDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options);

    private static void AddRole(
        FlitDbContext db,
        string code,
        string target = "COMPANY",
        bool deleted = false) =>
        db.Roles.Add(new Role
        {
            Id = Guid.CreateVersion7(),
            Code = code,
            Name = code,
            TargetEntityType = target,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
            DeletedAt = deleted ? DateTimeOffset.UtcNow : null,
        });

    private static async Task<List<string>> GrantedRoleCodesAsync(
        FlitDbContext db,
        Guid permissionId,
        CancellationToken ct)
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

    /// <summary>Invoca el método privado <c>SeedConsolidadoMasivoPermissionsAsync</c> por reflexión.</summary>
    private static async Task InvokeSeedAsync(FlitDbContext db, CancellationToken ct)
    {
        var method = typeof(DevelopmentAuthSeeder).GetMethod(
            "SeedConsolidadoMasivoPermissionsAsync",
            BindingFlags.NonPublic | BindingFlags.Static);

        method.Should().NotBeNull("el seed del módulo consolidado-masivo debe existir en DevelopmentAuthSeeder");

        await (Task)method!.Invoke(null, [db, ct])!;
    }
}
