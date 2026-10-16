using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Sql;
using Flit.Integration.Tests.Postgres;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Xunit;

namespace Flit.Integration.Tests.Security;

/// <summary>
/// HU #13638 (Feature #13100 FA-2, Épica #12753) contra PostgreSQL real: los 8 slugs <c>ot.*.manage</c> del Anexo A
/// (7 módulos de producto tramites) llegan por la migración 20261016150000, se conceden a ot_admin y son
/// idempotentes. El DDL 137 se reaplica porque el arnés trunca security.modules/permissions entre pruebas.
/// <para>
/// Uso de ejemplo: <c>await RunDdlAsync(cn); (await CountSlugsAsync(cn)).Should().Be(8);</c>
/// </para>
/// </summary>
public sealed class Hu13638SlugsOtMigrationTests(PostgresDatabaseFixture fixture) : PostgresTestBase(fixture)
{
    private const string DdlFile = "137-HU13638-slugs-ot.sql";
    private const string MigrationId = "20261016150000_HU13638_SlugsOt";

    // Anexo A, Parte 1.1 (slug, módulo).
    private static readonly (string Slug, string Module)[] Expected =
    [
        ("ot.tramites.manage", "ot-tramites"),
        ("ot.improntas.manage", "ot-improntas"),
        ("ot.reportes.manage", "ot-reportes"),
        ("ot.mandatos.manage", "ot-mandatos"),
        ("ot.mandatarios.manage", "ot-mandatos"),
        ("ot.documentos.manage", "ot-documentos"),
        ("ot.usuarios.manage", "ot-usuarios"),
        ("ot.integracion.manage", "ot-integracion"),
    ];

    private bool _downgraded;

    public override async ValueTask DisposeAsync()
    {
        if (_downgraded && PostgresAvailability.IsAvailable)
        {
            await using var ctx = NewContext();
            await ctx.GetService<IMigrator>().MigrateAsync(null, TestContext.Current.CancellationToken);
        }

        await base.DisposeAsync();
    }

    private static async Task ExecAsync(NpgsqlConnection cn, string sql)
    {
        await using var cmd = new NpgsqlCommand(sql, cn);
        await cmd.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    private static async Task<T?> ScalarAsync<T>(NpgsqlConnection cn, string sql)
    {
        await using var cmd = new NpgsqlCommand(sql, cn);
        var value = await cmd.ExecuteScalarAsync(TestContext.Current.CancellationToken);
        return value is null or DBNull ? default : (T)value;
    }

    private static Task RunDdlAsync(NpgsqlConnection cn) => ExecAsync(cn, EmbeddedDdl.LoadUp(DdlFile));

    private static async Task SeedRolesAsync(NpgsqlConnection cn) =>
        await ExecAsync(cn, """
            INSERT INTO security.roles (code, name, is_system, is_active, target_entity_type, product_code)
            VALUES ('SuperAdmin', 'SuperAdmin', true, true, 'COMPANY', 'plataforma'),
                   ('AdminCompany', 'AdminCompany', true, true, 'COMPANY', 'plataforma'),
                   ('ot_admin', 'Administrador OT', true, true, 'TRANSIT_OFFICE', 'tramites'),
                   ('gestor_tramites_ot', 'Operador OT', false, true, 'TRANSIT_OFFICE', 'tramites');
            """);

    private static Task<long> CountAsync(NpgsqlConnection cn, string sql) => ScalarAsync<long>(cn, sql);

    private const string SlugList =
        "('ot.tramites.manage','ot.improntas.manage','ot.reportes.manage','ot.mandatos.manage'," +
        "'ot.mandatarios.manage','ot.documentos.manage','ot.usuarios.manage','ot.integracion.manage')";

    [PostgresFact]
    public async Task Migracion_SeDescubreConAtributosInline()
    {
        await using var ctx = NewContext();
        ctx.Database.GetMigrations().Should().Contain(MigrationId);
        (await ctx.Database.GetAppliedMigrationsAsync(TestContext.Current.CancellationToken)).Should().Contain(MigrationId);
    }

    // AC1: al aplicar la migración, cada slug existe con su módulo y producto y está asignado a ot_admin.
    [PostgresFact]
    public async Task AC1_Migracion_CreaSlugsConModuloYProductoYLosAsignaAOtAdmin()
    {
        await using var cn = await Fixture.OpenConnectionAsync();
        await SeedRolesAsync(cn);

        // Camino real: Down de la migración y Up de nuevo (el catálogo ya estaba truncado por el reset).
        _downgraded = true;
        await using (var ctx = NewContext())
        {
            var all = ctx.Database.GetMigrations().ToList();
            var previous = all[all.IndexOf(MigrationId) - 1];
            var migrator = ctx.GetService<IMigrator>();
            await migrator.MigrateAsync(previous, TestContext.Current.CancellationToken);
            (await CountAsync(cn, $"SELECT count(*) FROM security.permissions WHERE slug IN {SlugList}")).Should().Be(0);
            await migrator.MigrateAsync(null, TestContext.Current.CancellationToken);
            _downgraded = false;
        }

        foreach (var (slug, module) in Expected)
        {
            (await CountAsync(cn, $"""
                SELECT count(*) FROM security.permissions p
                  JOIN security.modules m ON m.id = p.module_id
                 WHERE p.slug = '{slug}' AND m.code = '{module}' AND m.product_code = 'tramites' AND p.is_active
                """)).Should().Be(1, $"{slug} debe colgar de {module} (producto tramites)");

            (await CountAsync(cn, $"""
                SELECT count(*) FROM security.role_permissions rp
                  JOIN security.permissions p ON p.id = rp.permission_id
                  JOIN security.roles r ON r.id = rp.role_id
                 WHERE p.slug = '{slug}' AND r.code = 'ot_admin' AND r.target_entity_type = 'TRANSIT_OFFICE'
                """)).Should().Be(1, $"{slug} debe estar asignado a ot_admin");
        }

        (await CountAsync(cn, "SELECT count(*) FROM security.modules WHERE code LIKE 'ot-%'")).Should().Be(7);

        // Alcance: no se asigna a roles OT no-admin ni a AdminCompany (eso es #13639 / prohibido por producto).
        (await CountAsync(cn, """
            SELECT count(*) FROM security.role_permissions rp
              JOIN security.roles r ON r.id = rp.role_id
              JOIN security.permissions p ON p.id = rp.permission_id
             WHERE p.slug LIKE 'ot.%' AND r.code NOT IN ('ot_admin', 'SuperAdmin')
            """)).Should().Be(0);
    }

    // AC2: reejecutar el DDL deja catálogo y grants idénticos.
    [PostgresFact]
    public async Task AC2_Ddl_EsIdempotente_SegundaEjecucionNoDuplicaNiCambiaNada()
    {
        await using var cn = await Fixture.OpenConnectionAsync();
        await SeedRolesAsync(cn);
        await RunDdlAsync(cn);

        const string snapshotSql = """
            SELECT coalesce(string_agg(x, '|' ORDER BY x), '') FROM (
              SELECT 'm:' || m.id || m.code || m.sort_order || m.product_code AS x FROM security.modules m WHERE m.code LIKE 'ot-%'
              UNION ALL
              SELECT 'p:' || p.id || p.slug || p.module_id || p.route_pattern FROM security.permissions p WHERE p.slug LIKE 'ot.%'
              UNION ALL
              SELECT 'g:' || rp.id || rp.role_id || rp.permission_id FROM security.role_permissions rp
                JOIN security.permissions p ON p.id = rp.permission_id WHERE p.slug LIKE 'ot.%'
            ) s
            """;
        var first = await ScalarAsync<string>(cn, snapshotSql);

        await RunDdlAsync(cn);
        var second = await ScalarAsync<string>(cn, snapshotSql);

        second.Should().Be(first);
        (await CountAsync(cn, $"SELECT count(*) FROM security.permissions WHERE slug IN {SlugList}")).Should().Be(8);
        (await CountAsync(cn, "SELECT count(*) FROM security.modules WHERE code LIKE 'ot-%'")).Should().Be(7);
        // 8 slugs x (ot_admin + SuperAdmin)
        (await CountAsync(cn, "SELECT count(*) FROM security.role_permissions rp JOIN security.permissions p ON p.id = rp.permission_id WHERE p.slug LIKE 'ot.%'"))
            .Should().Be(16);
    }

    // AC3: base nueva sin datos de desarrollo -> los slugs vienen de la migración, no del seeder.
    [PostgresFact]
    public async Task AC3_BaseNuevaSinRoles_LaMigracionCreaElCatalogo_YElSeederNoLosSiembra()
    {
        await using var cn = await Fixture.OpenConnectionAsync();
        (await CountAsync(cn, "SELECT count(*) FROM security.roles WHERE code = 'ot_admin'")).Should().Be(0);

        await RunDdlAsync(cn); // sin ot_admin: el catálogo igualmente nace (sin tronar)
        (await CountAsync(cn, $"SELECT count(*) FROM security.permissions WHERE slug IN {SlugList}")).Should().Be(8);

        var seeder = FindUp("src/Flit.Infrastructure/Security/DevelopmentAuthSeeder.cs");
        var text = await File.ReadAllTextAsync(seeder, TestContext.Current.CancellationToken);
        foreach (var (slug, module) in Expected)
        {
            text.Should().NotContain(slug, "los slugs OT los siembra solo la migración (HU #13638 AC3)");
            text.Should().NotContain($"\"{module}\"");
        }
    }

    // AC4: un slug OT (tramites) no se puede asignar a un rol de otro producto.
    [PostgresFact]
    public async Task AC4_SlugOtNoSeAsignaARolDePlataforma_TriggerSameProductLoRechaza()
    {
        await using var cn = await Fixture.OpenConnectionAsync();
        await SeedRolesAsync(cn);
        await RunDdlAsync(cn);

        var act = async () => await ExecAsync(cn, """
            INSERT INTO security.role_permissions (role_id, permission_id)
            SELECT r.id, p.id FROM security.roles r, security.permissions p
             WHERE r.code = 'AdminCompany' AND p.slug = 'ot.tramites.manage'
            """);

        var ex = (await act.Should().ThrowAsync<PostgresException>()).Which;
        ex.ConstraintName.Should().Be("ck_role_permissions_same_product");
    }

    private static string FindUp(string relativeUnderCoreApi)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, relativeUnderCoreApi);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        throw new FileNotFoundException(relativeUnderCoreApi);
    }
}
