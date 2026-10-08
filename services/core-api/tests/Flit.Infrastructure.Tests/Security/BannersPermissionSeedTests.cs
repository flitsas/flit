using System.Reflection;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Security;
using Flit.Infrastructure.Security;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Flit.Infrastructure.Tests.Security;

/// <summary>
/// HU #13438 (Feature #13436, Épica #12750) — AC4: el seeder concede <c>banners.manage</c> SOLO a SuperAdmin y
/// converge las bases ya sembradas retirando el grant de AdminCompany y de roles personalizados.
/// </summary>
/// <remarks>Se ejercita solo el método por reflexión: <c>SeedAsync</c> ejecuta SQL crudo que InMemory no soporta.</remarks>
public sealed class BannersPermissionSeedTests
{
    private const string Slug = "banners.manage";

    [Fact]
    public async Task AC4_BaseNueva_ConcedeBannersManageSoloASuperAdmin()
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

        await using var check = NewContext(dbName);
        (await GrantedRoleCodesAsync(check, ct)).Should().BeEquivalentTo(["SuperAdmin"]);
    }

    [Fact]
    public async Task AC3_BaseYaSembrada_RetiraElGrantDeAdminCompanyYDeRolesPersonalizados()
    {
        var ct = TestContext.Current.CancellationToken;
        var dbName = Guid.NewGuid().ToString();

        // Estado heredado: el seeder anterior concedió el permiso a SuperAdmin y AdminCompany, y un admin
        // además se lo asignó a un rol personalizado.
        await InvokeSeedAsyncOnNewContext(dbName, seedRoles: ["SuperAdmin"], ct);
        await using (var legacy = NewContext(dbName))
        {
            var action = await legacy.RbacActions.SingleAsync(a => a.Slug == Slug, ct);
            var adminCompany = AddRole(legacy, "AdminCompany");
            var custom = AddRole(legacy, "Gerente");
            foreach (var role in new[] { adminCompany, custom })
            {
                legacy.RoleGrants.Add(new RoleGrant
                {
                    Id = Guid.CreateVersion7(),
                    RoleId = role.Id,
                    PermissionId = action.Id,
                    CreatedAt = DateTimeOffset.UtcNow,
                });
            }

            await legacy.SaveChangesAsync(ct);
        }

        await using (var db = NewContext(dbName))
        {
            await InvokeSeedAsync(db, ct);
        }

        await using var check = NewContext(dbName);
        (await GrantedRoleCodesAsync(check, ct)).Should().BeEquivalentTo(["SuperAdmin"]);
    }

    [Fact]
    public async Task AC3_EjecutadoDosVeces_EsIdempotente()
    {
        var ct = TestContext.Current.CancellationToken;
        var dbName = Guid.NewGuid().ToString();

        await InvokeSeedAsyncOnNewContext(dbName, seedRoles: ["SuperAdmin", "AdminCompany"], ct);
        await using (var second = NewContext(dbName))
        {
            await InvokeSeedAsync(second, ct);
        }

        await using var check = NewContext(dbName);
        (await check.RbacActions.CountAsync(a => a.Slug == Slug, ct)).Should().Be(1);
        (await check.RoleGrants.CountAsync(ct)).Should().Be(1, "un segundo pase no duplica ni revive grants");
    }

    private static async Task InvokeSeedAsyncOnNewContext(string dbName, string[] seedRoles, CancellationToken ct)
    {
        await using (var seed = NewContext(dbName))
        {
            foreach (var code in seedRoles) AddRole(seed, code);
            await seed.SaveChangesAsync(ct);
        }

        await using var db = NewContext(dbName);
        await InvokeSeedAsync(db, ct);
    }

    private static FlitDbContext NewContext(string dbName) =>
        new(new DbContextOptionsBuilder<FlitDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options);

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

    private static async Task<List<string>> GrantedRoleCodesAsync(FlitDbContext db, CancellationToken ct)
    {
        var action = await db.RbacActions.SingleAsync(a => a.Slug == Slug, ct);
        var roleIds = await db.RoleGrants
            .Where(g => g.PermissionId == action.Id)
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
            "SeedBannersPermissionsAsync",
            BindingFlags.NonPublic | BindingFlags.Static);

        method.Should().NotBeNull("el seed de banners debe existir en DevelopmentAuthSeeder");

        await (Task)method!.Invoke(null, [db, ct])!;
    }
}
