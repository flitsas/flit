using System.Reflection;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Security;
using Flit.Infrastructure.Persistence.Repositories;
using Flit.Infrastructure.Security;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Flit.Infrastructure.Tests.Security;

/// <summary>
/// HU #13580 (Feature #13579, Épica #13271) — los permisos de Reportes Detallados pasan al módulo <c>reportes</c>
/// y el módulo <c>reportes-detallados</c> se retira con borrado lógico, sin que ningún rol pierda acceso.
/// </summary>
/// <remarks>Se ejercita solo el método por reflexión: <c>SeedAsync</c> ejecuta SQL crudo que InMemory no soporta.</remarks>
public sealed class ReportesDetalladosPermissionSeedTests
{
    private const string ReadSlug = "reportes.detallados.read";
    private const string ExportSlug = "reportes.detallados.export";

    [Fact]
    public async Task AC1_BaseYaSembrada_MueveLosPermisosAReportesYConservaLosGrants()
    {
        var ct = TestContext.Current.CancellationToken;
        var dbName = Guid.NewGuid().ToString();
        Guid gerenteId;

        await using (var legacy = NewContext(dbName))
        {
            AddModule(legacy, "reportes");
            var detallados = AddModule(legacy, "reportes-detallados");
            var gerente = AddRole(legacy, "Gerente");
            gerenteId = gerente.Id;
            foreach (var slug in new[] { ReadSlug, ExportSlug })
                Grant(legacy, gerente, AddAction(legacy, detallados, slug));
            await legacy.SaveChangesAsync(ct);
        }

        await InvokeSeedAsync(dbName, ct);

        await using var check = NewContext(dbName);
        var reportes = await check.SecurityModules.SingleAsync(m => m.Code == "reportes", ct);
        var actions = await check.RbacActions.Where(a => a.Slug == ReadSlug || a.Slug == ExportSlug).ToListAsync(ct);
        actions.Should().HaveCount(2).And.OnlyContain(a => a.ModuleId == reportes.Id);
        (await check.RoleGrants.Where(g => g.RoleId == gerenteId).Select(g => g.PermissionId).ToListAsync(ct))
            .Should().BeEquivalentTo(actions.Select(a => a.Id), "los grants apuntan al mismo permiso");
    }

    [Fact]
    public async Task AC2_RolSoloConDetallados_RecibeElModuloReportesYNoElAntiguo()
    {
        var ct = TestContext.Current.CancellationToken;
        var dbName = Guid.NewGuid().ToString();

        await using (var legacy = NewContext(dbName))
        {
            AddModule(legacy, "reportes");
            AddAction(legacy, AddModule(legacy, "reportes-detallados"), ReadSlug);
            await legacy.SaveChangesAsync(ct);
        }

        await InvokeSeedAsync(dbName, ct);

        await using var check = NewContext(dbName);
        var accessible = await new SecurityModuleRepository(check).ListAccessibleAsync([ReadSlug], includeAll: false, productCode: null, ct);
        accessible.Should().ContainSingle();
        accessible[0].Code.Should().Be("reportes");
        accessible[0].Actions.Select(a => a.Slug).Should().Equal(ReadSlug);
    }

    [Fact]
    public async Task AC3_ElModuloAntiguoQuedaConBorradoLogicoYFueraDelConstructorDeRoles()
    {
        var ct = TestContext.Current.CancellationToken;
        var dbName = Guid.NewGuid().ToString();

        await using (var legacy = NewContext(dbName))
        {
            AddModule(legacy, "reportes");
            AddAction(legacy, AddModule(legacy, "reportes-detallados"), ReadSlug);
            await legacy.SaveChangesAsync(ct);
        }

        await InvokeSeedAsync(dbName, ct);

        await using var check = NewContext(dbName);
        var old = await check.SecurityModules.SingleAsync(m => m.Code == "reportes-detallados", ct);
        old.DeletedAt.Should().NotBeNull("el retiro es lógico, no físico");
        old.IsActive.Should().BeFalse();

        var all = await new SecurityModuleRepository(check).ListAccessibleAsync([], includeAll: true, productCode: null, ct);
        all.Select(m => m.Code).Should().NotContain("reportes-detallados");
        all.Single(m => m.Code == "reportes").Actions.Select(a => a.Slug).Should().Contain([ReadSlug, ExportSlug]);
    }

    [Fact]
    public async Task AC4_SinElPermiso_ElModuloNoEsAccesible()
    {
        var ct = TestContext.Current.CancellationToken;
        var dbName = Guid.NewGuid().ToString();

        await using (var seed = NewContext(dbName))
        {
            AddModule(seed, "reportes");
            await seed.SaveChangesAsync(ct);
        }

        await InvokeSeedAsync(dbName, ct);

        await using var check = NewContext(dbName);
        (await new SecurityModuleRepository(check).ListAccessibleAsync(["tramites.read"], includeAll: false, productCode: null, ct))
            .Should().BeEmpty("sin reportes.detallados.read no se abre ningún módulo de reportes");
    }

    [Fact]
    public async Task AC5_BaseNueva_SiembraEnReportesYNoCreaElModuloAntiguo()
    {
        var ct = TestContext.Current.CancellationToken;
        var dbName = Guid.NewGuid().ToString();

        await using (var seed = NewContext(dbName))
        {
            AddModule(seed, "reportes");
            AddRole(seed, "SuperAdmin");
            await seed.SaveChangesAsync(ct);
        }

        await InvokeSeedAsync(dbName, ct);

        await using var check = NewContext(dbName);
        var reportes = await check.SecurityModules.SingleAsync(m => m.Code == "reportes", ct);
        (await check.RbacActions.Where(a => a.ModuleId == reportes.Id).Select(a => a.Slug).ToListAsync(ct))
            .Should().BeEquivalentTo([ReadSlug, ExportSlug]);
        (await check.SecurityModules.AnyAsync(m => m.Code == "reportes-detallados", ct)).Should().BeFalse();
        (await check.RoleGrants.CountAsync(ct)).Should().Be(2, "SuperAdmin recibe los dos permisos");
    }

    [Fact]
    public async Task AC5_EjecutadoDosVeces_EsIdempotente()
    {
        var ct = TestContext.Current.CancellationToken;
        var dbName = Guid.NewGuid().ToString();

        await using (var legacy = NewContext(dbName))
        {
            AddModule(legacy, "reportes");
            AddRole(legacy, "SuperAdmin");
            AddAction(legacy, AddModule(legacy, "reportes-detallados"), ReadSlug);
            await legacy.SaveChangesAsync(ct);
        }

        await InvokeSeedAsync(dbName, ct);
        await InvokeSeedAsync(dbName, ct);

        await using var check = NewContext(dbName);
        (await check.RbacActions.CountAsync(ct)).Should().Be(2);
        (await check.SecurityModules.CountAsync(m => m.Code == "reportes-detallados", ct)).Should().Be(1, "no se recrea");
        (await check.RoleGrants.CountAsync(ct)).Should().Be(1, "solo el permiso nuevo (export) se concede; no se duplica");
    }

    private static FlitDbContext NewContext(string dbName) =>
        new(new DbContextOptionsBuilder<FlitDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options);

    private static SecurityModule AddModule(FlitDbContext db, string code)
    {
        var module = new SecurityModule
        {
            Id = Guid.CreateVersion7(),
            Code = code,
            Name = code,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        db.SecurityModules.Add(module);
        return module;
    }

    private static RbacAction AddAction(FlitDbContext db, SecurityModule module, string slug)
    {
        var action = new RbacAction
        {
            Id = Guid.CreateVersion7(),
            ModuleId = module.Id,
            Slug = slug,
            Name = slug,
            HttpMethod = "GET",
            RoutePattern = "/api/v1/detailed-report/procedures",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        db.RbacActions.Add(action);
        return action;
    }

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

    private static void Grant(FlitDbContext db, Role role, RbacAction action) =>
        db.RoleGrants.Add(new RoleGrant
        {
            Id = Guid.CreateVersion7(),
            RoleId = role.Id,
            PermissionId = action.Id,
            CreatedAt = DateTimeOffset.UtcNow,
        });

    private static async Task InvokeSeedAsync(string dbName, CancellationToken ct)
    {
        var method = typeof(DevelopmentAuthSeeder).GetMethod(
            "SeedDetailedReportPermissionsAsync",
            BindingFlags.NonPublic | BindingFlags.Static);

        method.Should().NotBeNull("el seed de Reportes Detallados debe existir en DevelopmentAuthSeeder");

        await using var db = NewContext(dbName);
        await (Task)method!.Invoke(null, [db, ct])!;
    }
}
