using System.Reflection;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Security;
using Flit.Infrastructure.Security;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Flit.Infrastructure.Tests.Security;

/// <summary>
/// HU #13638 (Feature #13100, Épica #12753) — en una base nueva la migración de los slugs OT corre antes de que el
/// seeder cree el rol global <c>ot_admin</c>, así que el seeder le concede los permisos de los módulos <c>ot-*</c>
/// al crearlo.
/// </summary>
/// <remarks>
/// Uso de ejemplo: <c>GrantOtModulePermissionsAsync(db, otAdminRoleId, ct)</c> tras <c>db.Roles.Add(otAdmin)</c>.
/// </remarks>
public sealed class SeederOtAdminGrantsTests
{
    [Fact]
    public async Task OtAdminRecienCreado_RecibeSoloLosPermisosDeLosModulosOt()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = NewContext();
        var role = AddRole(db, "ot_admin");
        var otTramites = AddModule(db, "ot-tramites");
        var otUsuarios = AddModule(db, "ot-usuarios");
        var reportes = AddModule(db, "reportes");
        AddAction(db, otTramites, "ot.tramites.manage");
        AddAction(db, otUsuarios, "ot.usuarios.manage");
        AddAction(db, reportes, "reportes.read");
        await db.SaveChangesAsync(ct);

        await InvokeAsync(db, role.Id, ct);

        (await GrantedSlugsAsync(db, role.Id, ct)).Should().BeEquivalentTo(["ot.tramites.manage", "ot.usuarios.manage"]);
    }

    [Fact]
    public async Task InvocarDosVeces_NoDuplicaGrants()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = NewContext();
        var role = AddRole(db, "ot_admin");
        AddAction(db, AddModule(db, "ot-reportes"), "ot.reportes.manage");
        await db.SaveChangesAsync(ct);

        await InvokeAsync(db, role.Id, ct);
        await InvokeAsync(db, role.Id, ct);

        (await GrantedSlugsAsync(db, role.Id, ct)).Should().ContainSingle().Which.Should().Be("ot.reportes.manage");
    }

    [Fact]
    public async Task SinModulosOt_NoConcedeNada()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = NewContext();
        var role = AddRole(db, "ot_admin");
        AddAction(db, AddModule(db, "reportes"), "reportes.read");
        await db.SaveChangesAsync(ct);

        await InvokeAsync(db, role.Id, ct);

        (await GrantedSlugsAsync(db, role.Id, ct)).Should().BeEmpty();
    }

    private static async Task InvokeAsync(FlitDbContext db, Guid roleId, CancellationToken ct)
    {
        var method = typeof(DevelopmentAuthSeeder).GetMethod(
            "GrantOtModulePermissionsAsync", BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();
        await (Task)method!.Invoke(null, [db, roleId, ct])!;
    }

    private static async Task<List<string>> GrantedSlugsAsync(FlitDbContext db, Guid roleId, CancellationToken ct) =>
        await (
            from g in db.RoleGrants
            join a in db.RbacActions on g.PermissionId equals a.Id
            where g.RoleId == roleId
            select a.Slug).ToListAsync(ct);

    private static FlitDbContext NewContext() =>
        new(new DbContextOptionsBuilder<FlitDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static Role AddRole(FlitDbContext db, string code)
    {
        var role = new Role
        {
            Id = Guid.CreateVersion7(),
            Code = code,
            Name = code,
            TargetEntityType = "TRANSIT_OFFICE",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        db.Roles.Add(role);
        return role;
    }

    private static Guid AddModule(FlitDbContext db, string code)
    {
        var module = new SecurityModule
        {
            Id = Guid.CreateVersion7(),
            Code = code,
            Name = code,
            SortOrder = 1,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        db.SecurityModules.Add(module);
        return module.Id;
    }

    private static void AddAction(FlitDbContext db, Guid moduleId, string slug) =>
        db.RbacActions.Add(new RbacAction
        {
            Id = Guid.CreateVersion7(),
            ModuleId = moduleId,
            Slug = slug,
            Name = slug,
            HttpMethod = "GET",
            RoutePattern = "/api/v1/admin/ot",
            CreatedAt = DateTimeOffset.UtcNow,
        });
}
