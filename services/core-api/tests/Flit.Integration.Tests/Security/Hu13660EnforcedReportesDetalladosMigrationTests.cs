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
/// HU #13660 (Feature #13105 F-E, Épica #12753) contra PostgreSQL real: el DDL 141 marca enforced='true' los dos slugs de
/// Reportes detallados, completa los grants de admin_tramites, es idempotente y su Down devuelve el marcador a 'false'.
/// <para>Uso de ejemplo: <c>await SeedAsync(cn); await ExecAsync(cn, EmbeddedDdl.LoadUp(DdlFile)); await EnforcedAsync(cn, "true") == 2</c></para>
/// </summary>
public sealed class Hu13660EnforcedReportesDetalladosMigrationTests(PostgresDatabaseFixture fixture) : PostgresTestBase(fixture)
{
    private const string DdlFile = "141-HU13660-enforced-reportes-detallados.sql";
    private const string MigrationId = "20261019110000_HU13660_EnforcedReportesDetallados";
    private const int SlugCount = 2;

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

    private static async Task<long> CuentaAsync(NpgsqlConnection cn, string sql)
    {
        await using var cmd = new NpgsqlCommand(sql, cn);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync(TestContext.Current.CancellationToken));
    }

    // El arnés trunca security.permissions entre pruebas: se siembran módulo (producto tramites), permisos y rol.
    private static async Task SeedAsync(NpgsqlConnection cn)
    {
        await ExecAsync(cn, """
            INSERT INTO security.modules (code, name, sort_order, is_active, product_code)
            VALUES ('m13660', 'Módulo 13660', 9999, true, 'tramites') ON CONFLICT DO NOTHING;
            INSERT INTO security.permissions (module_id, slug, name, http_method, route_pattern, is_active)
            SELECT m.id, s, s, 'GET', '/x', true
              FROM security.modules m, unnest(ARRAY['reportes.detallados.read','reportes.detallados.export']) s
             WHERE m.code = 'm13660';
            """);
        if (await CuentaAsync(cn, "SELECT count(*) FROM security.roles WHERE code = 'admin_tramites' AND deleted_at IS NULL") == 0)
        {
            await ExecAsync(cn, """
                INSERT INTO security.roles (code, name, target_entity_type, product_code)
                VALUES ('admin_tramites', 'Administrador de Trámites', 'COMPANY', 'tramites');
                """);
        }
    }

    private static Task<long> EnforcedAsync(NpgsqlConnection cn, string valor) =>
        CuentaAsync(cn, $"SELECT count(*) FROM security.permissions WHERE slug LIKE 'reportes.detallados.%' AND enforced = '{valor}'");

    private static Task<long> GrantsAsync(NpgsqlConnection cn) =>
        CuentaAsync(cn, """
            SELECT count(*) FROM security.role_permissions rp
              JOIN security.roles r ON r.id = rp.role_id AND r.code = 'admin_tramites'
              JOIN security.permissions p ON p.id = rp.permission_id AND p.slug LIKE 'reportes.detallados.%'
            """);

    [PostgresFact]
    public async Task Migracion_SeDescubreConAtributosInline()
    {
        await using var ctx = NewContext();
        ctx.Database.GetMigrations().Should().Contain(MigrationId);
        (await ctx.Database.GetAppliedMigrationsAsync(TestContext.Current.CancellationToken)).Should().Contain(MigrationId);
    }

    [PostgresFact]
    public async Task Up_MarcaTrueLosDosSlugs_YCompletaLosGrantsDeAdminTramites()
    {
        await using var cn = await Fixture.OpenConnectionAsync();
        await SeedAsync(cn);
        (await EnforcedAsync(cn, "false")).Should().Be(SlugCount);

        await ExecAsync(cn, EmbeddedDdl.LoadUp(DdlFile));

        (await EnforcedAsync(cn, "true")).Should().Be(SlugCount);
        (await GrantsAsync(cn)).Should().Be(SlugCount);
    }

    [PostgresFact]
    public async Task Ddl_EsIdempotente()
    {
        await using var cn = await Fixture.OpenConnectionAsync();
        await SeedAsync(cn);
        await ExecAsync(cn, EmbeddedDdl.LoadUp(DdlFile));
        await ExecAsync(cn, EmbeddedDdl.LoadUp(DdlFile));

        (await EnforcedAsync(cn, "true")).Should().Be(SlugCount);
        (await GrantsAsync(cn)).Should().Be(SlugCount);
    }

    [PostgresFact]
    public async Task Down_DevuelveElMarcadorAFalse()
    {
        await using var cn = await Fixture.OpenConnectionAsync();
        await SeedAsync(cn);
        await ExecAsync(cn, EmbeddedDdl.LoadUp(DdlFile));
        _downgraded = true;

        await using (var ctx = NewContext())
        {
            var all = ctx.Database.GetMigrations().ToList();
            await ctx.GetService<IMigrator>().MigrateAsync(all[all.IndexOf(MigrationId) - 1], TestContext.Current.CancellationToken);
        }

        (await EnforcedAsync(cn, "false")).Should().Be(SlugCount);
    }
}
