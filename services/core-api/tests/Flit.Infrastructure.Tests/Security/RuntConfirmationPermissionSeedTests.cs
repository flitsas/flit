using System.Reflection;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Security;
using Flit.Infrastructure.Security;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Flit.Infrastructure.Tests.Security;

/// <summary>
/// HU #12313 (Feature #12276) — alta del módulo <c>confirmacion-runt</c> y de los permisos
/// <c>runt_confirmation.settings.manage</c> y <c>runt_confirmation.history.read</c> en el catálogo RBAC.
/// </summary>
/// <remarks>
/// Se ejercita SOLO el método privado por reflexión: <c>SeedAsync</c> ejecuta scripts SQL crudos que el
/// proveedor InMemory no soporta. Lo que se protege es que ambos permisos queden en el catálogo (para que
/// SuperAdmin pueda conceder el historial sin la configuración, AC2) y que el seed sea idempotente.
/// </remarks>
public sealed class RuntConfirmationPermissionSeedTests
{
    private const string ModuleCode = "confirmacion-runt";
    private const string ManageSlug = "runt_confirmation.settings.manage";
    private const string HistorySlug = "runt_confirmation.history.read";

    [Fact]
    public async Task Seed_CreaModuloYLosDosPermisosConcedidosASuperAdmin()
    {
        var ct = TestContext.Current.CancellationToken;
        var dbName = Guid.NewGuid().ToString();

        await using (var seed = NewContext(dbName))
        {
            AddRole(seed, "SuperAdmin");
            AddRole(seed, "AdminCompany");
            await seed.SaveChangesAsync(ct);
        }

        await using (var db = NewContext(dbName))
        {
            await InvokeSeedAsync(db, ct);
        }

        await using (var check = NewContext(dbName))
        {
            var module = await check.SecurityModules.SingleAsync(m => m.Code == ModuleCode, ct);
            module.Name.Should().Be("Plataforma · Confirmación RUNT");
            module.IsActive.Should().BeTrue();

            var manage = await check.RbacActions.SingleAsync(a => a.Slug == ManageSlug, ct);
            manage.ModuleId.Should().Be(module.Id);
            manage.HttpMethod.Should().Be("PUT");
            manage.RoutePattern.Should().Be("/api/v1/admin/runt-confirmation/settings");

            var history = await check.RbacActions.SingleAsync(a => a.Slug == HistorySlug, ct);
            history.ModuleId.Should().Be(module.Id);
            history.HttpMethod.Should().Be("GET");
            history.RoutePattern.Should().Be("/api/v1/admin/runt-confirmation/history");

            (await GrantedRoleCodesAsync(check, manage.Id, ct)).Should().BeEquivalentTo(["SuperAdmin"],
                "AdminCompany no recibe el permiso por defecto: concederlo es un acto explícito de RBAC");
            (await GrantedRoleCodesAsync(check, history.Id, ct)).Should().BeEquivalentTo(["SuperAdmin"]);
        }
    }

    [Fact]
    public async Task Seed_EjecutadoDosVeces_NoDuplicaModuloPermisosNiGrants()
    {
        var ct = TestContext.Current.CancellationToken;
        var dbName = Guid.NewGuid().ToString();

        await using (var seed = NewContext(dbName))
        {
            AddRole(seed, "SuperAdmin");
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
            (await check.RbacActions.CountAsync(a => a.Slug == ManageSlug, ct)).Should().Be(1);
            (await check.RbacActions.CountAsync(a => a.Slug == HistorySlug, ct)).Should().Be(1);

            var actionIds = await check.RbacActions
                .Where(a => a.Slug == ManageSlug || a.Slug == HistorySlug)
                .Select(a => a.Id)
                .ToListAsync(ct);
            (await check.RoleGrants.CountAsync(g => actionIds.Contains(g.PermissionId), ct))
                .Should().Be(2, "un segundo pase no debe volver a conceder lo ya concedido");
        }
    }

    [Fact]
    public async Task Seed_SinRolSuperAdmin_DejaLosPermisosEnElCatalogo()
    {
        var ct = TestContext.Current.CancellationToken;
        var dbName = Guid.NewGuid().ToString();

        await using (var seed = NewContext(dbName))
        {
            AddRole(seed, "SuperAdmin", deleted: true);
            await seed.SaveChangesAsync(ct);
        }

        await using (var db = NewContext(dbName))
        {
            await InvokeSeedAsync(db, ct);
        }

        await using (var check = NewContext(dbName))
        {
            (await check.RbacActions.CountAsync(a => a.Slug == ManageSlug || a.Slug == HistorySlug, ct))
                .Should().Be(2);
            (await check.RoleGrants.CountAsync(ct)).Should().Be(0,
                "un rol borrado lógicamente no recibe grant, pero el catálogo queda poblado");
        }
    }

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

    private static async Task InvokeSeedAsync(FlitDbContext db, CancellationToken ct)
    {
        var method = typeof(DevelopmentAuthSeeder).GetMethod(
            "SeedRuntConfirmationPermissionsAsync",
            BindingFlags.NonPublic | BindingFlags.Static);

        method.Should().NotBeNull("el seed del módulo confirmacion-runt debe existir en DevelopmentAuthSeeder");

        await (Task)method!.Invoke(null, [db, ct])!;
    }
}
