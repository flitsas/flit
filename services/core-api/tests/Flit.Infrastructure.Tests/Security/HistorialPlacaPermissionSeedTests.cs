using System.Reflection;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Security;
using Flit.Infrastructure.Security;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Flit.Infrastructure.Tests.Security;

/// <summary>
/// HU #12191 (Feature #12189) — registro del módulo <c>historial-placa</c> y del permiso
/// <c>historial-placa.read</c> en el catálogo RBAC.
/// </summary>
/// <remarks>
/// <para>
/// Uso de ejemplo (producción): <c>DevelopmentAuthSeeder.SeedAsync(db, hasher, env, ct)</c> invoca
/// internamente el seed del historial por placa. Aquí se ejercita SOLO ese método por reflexión,
/// porque <c>SeedAsync</c> ejecuta scripts SQL crudos que el proveedor InMemory no soporta y
/// arrastraría todo el seed de DEV a una prueba que solo mira el catálogo RBAC.
/// </para>
/// <para>
/// Lo que se protege aquí es que el módulo quede gobernado por PERMISO y no por nombre de rol:
/// el permiso debe existir en el catálogo (para que SuperAdmin pueda asignarlo a cualquier rol del
/// ambiente) y quedar concedido a los tres roles de la decisión D4 del PO.
/// </para>
/// </remarks>
public sealed class HistorialPlacaPermissionSeedTests
{
    private const string ModuleCode = "historial-placa";
    private const string ActionSlug = "historial-placa.read";

    // ── Happy path: módulo + permiso + grants a los tres roles de D4 ───────────────────

    [Fact]
    public async Task Seed_CreaModuloPermisoYLoConcedeALosTresRolesDeD4()
    {
        var ct = TestContext.Current.CancellationToken;
        var dbName = Guid.NewGuid().ToString();

        await using (var seed = NewContext(dbName))
        {
            AddRole(seed, "SuperAdmin");
            AddRole(seed, "AdminCompany");
            AddRole(seed, "Radicador");
            await seed.SaveChangesAsync(ct);
        }

        await using (var db = NewContext(dbName))
        {
            await InvokeSeedAsync(db, ct);
        }

        await using (var check = NewContext(dbName))
        {
            var module = await check.SecurityModules
                .SingleAsync(m => m.Code == ModuleCode, ct);
            module.Name.Should().Be("Historial por placa");
            module.IsActive.Should().BeTrue();
            module.SortOrder.Should().Be(11, "1..10 ya están ocupados por los módulos existentes");

            var action = await check.RbacActions.SingleAsync(a => a.Slug == ActionSlug, ct);
            action.ModuleId.Should().Be(module.Id, "el permiso debe colgar del módulo para ser administrable");
            action.Name.Should().Be("Ver historial por placa");
            action.HttpMethod.Should().Be("GET");
            action.RoutePattern.Should().Be("/api/v1/tramites/instances/plate-history");
            action.IsActive.Should().BeTrue();

            var grantedRoleCodes = await GrantedRoleCodesAsync(check, action.Id, ct);
            grantedRoleCodes.Should().BeEquivalentTo(["SuperAdmin", "AdminCompany", "Radicador"]);
        }
    }

    // ── Idempotencia: correr el seeder dos veces no duplica nada ───────────────────────

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
                .Should().Be(3, "un segundo pase no debe volver a conceder lo ya concedido");
        }
    }

    // ── Edge case: roles ausentes, borrados o ajenos no reciben el grant ───────────────

    [Fact]
    public async Task Seed_ConRolBorradoOInexistente_NoLesConcedeElPermiso()
    {
        var ct = TestContext.Current.CancellationToken;
        var dbName = Guid.NewGuid().ToString();

        await using (var seed = NewContext(dbName))
        {
            AddRole(seed, "SuperAdmin");
            AddRole(seed, "AdminCompany", deleted: true);
            // "Radicador" no existe en este ambiente.
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

    // ── Contrato: el permiso queda en el catálogo aunque no exista ningún rol de D4 ─────

    [Fact]
    public async Task Seed_SinNingunRolDeD4_DejaElPermisoEnElCatalogoParaAsignarloDespues()
    {
        var ct = TestContext.Current.CancellationToken;
        var dbName = Guid.NewGuid().ToString();

        await using (var db = NewContext(dbName))
        {
            await InvokeSeedAsync(db, ct);
        }

        await using (var check = NewContext(dbName))
        {
            var module = await check.SecurityModules.SingleAsync(m => m.Code == ModuleCode, ct);
            var action = await check.RbacActions.SingleAsync(a => a.Slug == ActionSlug, ct);

            action.ModuleId.Should().Be(module.Id);
            (await check.RoleGrants.CountAsync(g => g.PermissionId == action.Id, ct)).Should().Be(0,
                "sin roles no hay a quién conceder, pero el catálogo debe quedar poblado " +
                "para que SuperAdmin lo asigne desde la pantalla RBAC");
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────────────

    private static FlitDbContext NewContext(string dbName) =>
        new(new DbContextOptionsBuilder<FlitDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options);

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

    /// <summary>
    /// Invoca el método privado <c>SeedHistorialPlacaPermissionsAsync</c>. Es privado a propósito
    /// (el seeder expone solo <c>SeedAsync</c>), así que la prueba lo alcanza por reflexión en vez
    /// de ampliar la superficie pública solo para poder testear.
    /// </summary>
    private static async Task InvokeSeedAsync(FlitDbContext db, CancellationToken ct)
    {
        var method = typeof(DevelopmentAuthSeeder).GetMethod(
            "SeedHistorialPlacaPermissionsAsync",
            BindingFlags.NonPublic | BindingFlags.Static);

        method.Should().NotBeNull("el seed del módulo historial-placa debe existir en DevelopmentAuthSeeder");

        await (Task)method!.Invoke(null, [db, ct])!;
    }
}
