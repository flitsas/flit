using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Security;
using Flit.Integration.Tests.Postgres;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Flit.Integration.Tests.Platform;

/// <summary>
/// HU #12895 (FLIT Suite A-02) contra PostgreSQL real: el catálogo RBAC y las cuentas demo se siembran por separado.
/// Un ambiente que deja de llamarse Development sigue recibiendo roles, módulos y permisos sin crear cuentas con
/// contraseña fija (a-inventario-ambientes.md, puntos 7 a 9).
/// </summary>
public sealed class SeedSettingsTests(PostgresDatabaseFixture fixture) : PostgresTestBase(fixture)
{
    private static readonly SeedSettings CatalogOnly = new(RbacCatalog: true, DemoData: false);

    [PostgresFact]
    public async Task SoloCatalogo_CreaRolesDeSistemaModulosYOrganismos_SinCuentasDemo()
    {
        await SeedAsync(CatalogOnly);

        await using var db = NewContext();
        var systemRoles = await db.Roles
            .Where(r => r.Code == "SuperAdmin" || r.Code == "AdminCompany")
            .ToListAsync(TestContext.Current.CancellationToken);
        systemRoles.Select(r => r.Code).Should().BeEquivalentTo(["SuperAdmin", "AdminCompany"]);
        systemRoles.Should().OnlyContain(r => r.ProductCode == "plataforma" && r.IsSystem);

        (await db.SecurityModules.CountAsync(m => m.Code == "dashboard" || m.Code == "auth" || m.Code == "usuarios", TestContext.Current.CancellationToken))
            .Should().Be(3);
        (await PermissionsOfAsync(db, "SuperAdmin")).Should().Contain(["auth.me.read", "dashboard.read", "usuarios.manage"]);
        (await PermissionsOfAsync(db, "AdminCompany")).Should().Contain("usuarios.manage").And.NotContain("tramites.read");
        (await db.Database.SqlQueryRaw<int>("SELECT count(*)::int AS \"Value\" FROM catalogs.transit_offices").SingleAsync(TestContext.Current.CancellationToken))
            .Should().BeGreaterThan(0, "el catálogo RUNT de organismos es catálogo, no dato demo");

        (await db.Users.AnyAsync(u => u.Email.EndsWith("@flit.local") || u.Email.EndsWith("@empresa.local"), TestContext.Current.CancellationToken))
            .Should().BeFalse("sin Seed:DemoUsers no se crean cuentas con contraseña fija");
        (await db.Tenants.AnyAsync(t => t.Code == DevelopmentAuthSeeder.DemoTenantCode || t.Code == DevelopmentAuthSeeder.DemoEmpresaTenantCode, TestContext.Current.CancellationToken))
            .Should().BeFalse();
    }

    [PostgresFact]
    public async Task NadaEncendido_NoSiembraNada()
    {
        await SeedAsync(new SeedSettings(RbacCatalog: false, DemoData: false));

        await using var db = NewContext();
        (await db.Roles.AnyAsync(TestContext.Current.CancellationToken)).Should().BeFalse();
        (await db.SecurityModules.AnyAsync(TestContext.Current.CancellationToken)).Should().BeFalse();
        (await db.Users.AnyAsync(TestContext.Current.CancellationToken)).Should().BeFalse();
    }

    [PostgresFact]
    public async Task CatalogoDosVeces_NoDuplicaYReparaElProductoDeLosRolesDeSistema()
    {
        await SeedAsync(CatalogOnly);
        await using (var db = NewContext())
        {
            // Una base sembrada antes de A-02 dejaba AdminCompany con el producto por defecto de la entidad.
            await db.Roles.Where(r => r.Code == "AdminCompany")
                .ExecuteUpdateAsync(u => u.SetProperty(r => r.ProductCode, "tramites"), TestContext.Current.CancellationToken);
        }

        var before = await CountsAsync();
        await SeedAsync(CatalogOnly);

        (await CountsAsync()).Should().Be(before);
        await using var check = NewContext();
        (await check.Roles.SingleAsync(r => r.Code == "AdminCompany", TestContext.Current.CancellationToken))
            .ProductCode.Should().Be("plataforma");
    }

    // Las cuentas demo no se prueban aquí: sus SQL (12, 15 y 16) dependen de tenants que solo crean las migraciones de
    // datos demo, que la base efímera apaga. Las cubre Flit.Admin.Tests, que arranca la API como Development.

    [Fact]
    public void From_SinConfiguracion_SigueElNombreDelAmbiente()
    {
        var empty = new ConfigurationBuilder().Build();

        SeedSettings.From(empty, Environment("Development")).Should().Be(new SeedSettings(true, true));
        SeedSettings.From(empty, Environment("Dev")).Should().Be(new SeedSettings(false, false));
    }

    [Fact]
    public void From_ConConfiguracion_GanaLaConfiguracion()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Seed:RbacCatalog"] = "true", ["Seed:DemoUsers"] = "false" })
            .Build();

        SeedSettings.From(configuration, Environment("Dev")).Should().Be(CatalogOnly);
        SeedSettings.From(configuration, Environment("Development")).Should().Be(CatalogOnly);
    }

    private async Task SeedAsync(SeedSettings settings)
    {
        await using var db = NewContext();
        await DevelopmentAuthSeeder.SeedAsync(db, new Argon2PasswordHasher(), settings, TestContext.Current.CancellationToken);
    }

    private async Task<(int Roles, int Modules, int Permissions, int Grants, int Users, int Assignments)> CountsAsync()
    {
        await using var db = NewContext();
        var ct = TestContext.Current.CancellationToken;
        return (await db.Roles.CountAsync(ct), await db.SecurityModules.CountAsync(ct), await db.RbacActions.CountAsync(ct),
            await db.RoleGrants.CountAsync(ct), await db.Users.CountAsync(ct), await db.UserRoleAssignments.CountAsync(ct));
    }

    private static Task<List<string>> PermissionsOfAsync(FlitDbContext db, string roleCode) =>
        (from g in db.RoleGrants
         join r in db.Roles on g.RoleId equals r.Id
         join a in db.RbacActions on g.PermissionId equals a.Id
         where r.Code == roleCode
         select a.Slug).ToListAsync(TestContext.Current.CancellationToken);

    private static Task<List<string>> RolesOfAsync(FlitDbContext db, string email) =>
        (from u in db.Users
         join a in db.UserRoleAssignments on u.Id equals a.UserId
         join r in db.Roles on a.RoleId equals r.Id
         where u.Email == email && a.DeletedAt == null
         select r.Code).ToListAsync(TestContext.Current.CancellationToken);

    private static TestHostEnvironment Environment(string name) => new() { EnvironmentName = name };

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = string.Empty;
        public string ApplicationName { get; set; } = "Flit.Api";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
