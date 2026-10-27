using System.Reflection;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Security;
using Flit.Infrastructure.Security;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Flit.Infrastructure.Tests.Security;

/// <summary>
/// HU #13663 (Feature #13636, Épica #12753) — en una base nueva el DDL 145 corre antes de que el seeder conceda
/// <c>tramites.read</c> / <c>tramites.create</c>, así que el seeder concede los Ver/Crear por familia equivalentes.
/// </summary>
/// <remarks>
/// Uso de ejemplo: <c>GrantFamilyPermissionsLikeTramitesAsync(db, adminTramitesRoleId, ct)</c> tras sembrar el catálogo base.
/// </remarks>
public sealed class SeederFamilyPermissionsTests
{
    private static readonly string[] Familias =
        ["matriculas.read", "traspasos.read", "otros-tramites.read", "matriculas.create", "traspasos.create", "otros-tramites.create"];

    [Fact]
    public async Task RolConTramitesRead_RecibeSoloLosTresRead()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = NewContext();
        var (role, ids) = await SembrarAsync(db, ct);
        Conceder(db, role.Id, ids["tramites.read"]);
        await db.SaveChangesAsync(ct);

        await InvokeAsync(db, role.Id, ct);

        (await GrantedSlugsAsync(db, role.Id, ct)).Should().BeEquivalentTo(["tramites.read", "matriculas.read", "traspasos.read", "otros-tramites.read"]);
    }

    [Fact]
    public async Task RolConReadYCreate_RecibeLasSeisYNoDuplicaAlRepetir()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = NewContext();
        var (role, ids) = await SembrarAsync(db, ct);
        Conceder(db, role.Id, ids["tramites.read"]);
        Conceder(db, role.Id, ids["tramites.create"]);
        await db.SaveChangesAsync(ct);

        await InvokeAsync(db, role.Id, ct);
        await InvokeAsync(db, role.Id, ct);

        (await GrantedSlugsAsync(db, role.Id, ct)).Should().BeEquivalentTo(Familias.Append("tramites.read").Append("tramites.create"));
    }

    [Fact]
    public async Task RolSinTramites_NoRecibeNada()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = NewContext();
        var (role, _) = await SembrarAsync(db, ct);

        await InvokeAsync(db, role.Id, ct);

        (await GrantedSlugsAsync(db, role.Id, ct)).Should().BeEmpty();
    }

    private static async Task<(Role Role, Dictionary<string, Guid> Ids)> SembrarAsync(FlitDbContext db, CancellationToken ct)
    {
        var role = AddRole(db, "admin_tramites");
        var modulo = AddModule(db, "tramites");
        foreach (var slug in Familias.Append("tramites.read").Append("tramites.create"))
            AddAction(db, modulo, slug);
        await db.SaveChangesAsync(ct);
        var ids = await db.RbacActions.ToDictionaryAsync(a => a.Slug, a => a.Id, ct);
        return (role, ids);
    }

    private static void Conceder(FlitDbContext db, Guid roleId, Guid permissionId) =>
        db.RoleGrants.Add(new RoleGrant { Id = Guid.CreateVersion7(), RoleId = roleId, PermissionId = permissionId, CreatedAt = DateTimeOffset.UtcNow });

    private static async Task InvokeAsync(FlitDbContext db, Guid roleId, CancellationToken ct)
    {
        var method = typeof(DevelopmentAuthSeeder).GetMethod(
            "GrantFamilyPermissionsLikeTramitesAsync", BindingFlags.NonPublic | BindingFlags.Static);
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
            TargetEntityType = "COMPANY",
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
