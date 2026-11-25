using System.Reflection;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Security;
using Flit.Infrastructure.Security;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Flit.Infrastructure.Tests.Security;

/// <summary>
/// Épica #14644 (HU #14648) AC6 — el permiso <c>runt_contingency.manage</c> existe en el catálogo y se concede SOLO a SuperAdmin
/// (nunca a AdminCompany), y el seed es idempotente.
/// </summary>
public sealed class RuntContingencyPermissionSeedTests
{
    private const string Slug = "runt_contingency.manage";

    [Fact]
    public async Task Seed_CreaElModuloYElPermiso_ConcedidoSoloASuperAdmin_Idempotente()
    {
        var ct = TestContext.Current.CancellationToken;
        var dbName = Guid.NewGuid().ToString();
        await using (var seed = NewContext(dbName))
        {
            AddRole(seed, "SuperAdmin");
            AddRole(seed, "AdminCompany");
            await seed.SaveChangesAsync(ct);
        }

        for (var i = 0; i < 2; i++)
        {
            await using var db = NewContext(dbName);
            await InvokeSeedAsync(db, ct);
        }

        await using var check = NewContext(dbName);
        (await check.SecurityModules.CountAsync(m => m.Code == "contingencia-runt", ct)).Should().Be(1);
        var action = await check.RbacActions.SingleAsync(a => a.Slug == Slug, ct);
        action.HttpMethod.Should().Be("PUT");
        var roleIds = await check.RoleGrants.Where(g => g.PermissionId == action.Id).Select(g => g.RoleId).ToListAsync(ct);
        (await check.Roles.Where(r => roleIds.Contains(r.Id)).Select(r => r.Code).ToListAsync(ct)).Should().BeEquivalentTo(["SuperAdmin"]);
    }

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

    private static async Task InvokeSeedAsync(FlitDbContext db, CancellationToken ct)
    {
        var method = typeof(DevelopmentAuthSeeder).GetMethod("SeedRuntContingencyPermissionsAsync", BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();
        await (Task)method!.Invoke(null, [db, ct])!;
    }
}
