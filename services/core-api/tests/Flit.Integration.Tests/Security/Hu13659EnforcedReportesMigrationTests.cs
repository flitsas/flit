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
/// HU #13659 (Feature #13105 F-E, Épica #12753) contra PostgreSQL real: el DDL 140 marca <c>enforced = 'true'</c> los 8 slugs de
/// Reportes (AC3), no toca <c>reportes.detallados.*</c>, es idempotente y su Down los devuelve a 'false'.
/// <para>Uso de ejemplo: <c>await SeedAsync(cn); await ExecAsync(cn, EmbeddedDdl.LoadUp(DdlFile)); await ValorAsync(cn, "reportes.read") == "true"</c></para>
/// </summary>
public sealed class Hu13659EnforcedReportesMigrationTests(PostgresDatabaseFixture fixture) : PostgresTestBase(fixture)
{
    private const string DdlFile = "140-HU13659-enforced-reportes.sql";
    private const string MigrationId = "20261019100000_HU13659_EnforcedReportes";

    private static readonly string[] Slugs =
    [
        "reportes.read", "reportes.resumen.read", "reportes.operacion.read", "reportes.ot.read", "reportes.uso.read",
        "reportes.productividad.read", "reportes.consultas.read", "reportes.programacion.manage",
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

    private static async Task<string> ValorAsync(NpgsqlConnection cn, string slug)
    {
        await using var cmd = new NpgsqlCommand($"SELECT enforced FROM security.permissions WHERE slug = '{slug}'", cn);
        return (string)(await cmd.ExecuteScalarAsync(TestContext.Current.CancellationToken))!;
    }

    // El arnés trunca security.permissions entre pruebas: cada prueba siembra los 8 slugs y uno de otra HU.
    private static Task SeedAsync(NpgsqlConnection cn) => ExecAsync(cn, """
        INSERT INTO security.modules (code, name, sort_order, is_active, product_code)
        VALUES ('m13659', 'Módulo 13659', 9999, true, 'tramites') ON CONFLICT DO NOTHING;
        INSERT INTO security.permissions (module_id, slug, name, http_method, route_pattern, is_active)
        SELECT m.id, s, s, 'GET', '/x', true
          FROM security.modules m,
               unnest(ARRAY['reportes.read','reportes.resumen.read','reportes.operacion.read','reportes.ot.read','reportes.uso.read',
                            'reportes.productividad.read','reportes.consultas.read','reportes.programacion.manage','reportes.detallados.read']) s
         WHERE m.code = 'm13659';
        """);

    [PostgresFact]
    public async Task Migracion_SeDescubreConAtributosInline()
    {
        await using var ctx = NewContext();
        ctx.Database.GetMigrations().Should().Contain(MigrationId);
        (await ctx.Database.GetAppliedMigrationsAsync(TestContext.Current.CancellationToken)).Should().Contain(MigrationId);
    }

    // AC3
    [PostgresFact]
    public async Task AC3_LosOchoSlugsQuedanEnTrue_SinTocarLosDetallados()
    {
        await using var cn = await Fixture.OpenConnectionAsync();
        await SeedAsync(cn);
        (await ValorAsync(cn, "reportes.read")).Should().Be("false");

        await ExecAsync(cn, EmbeddedDdl.LoadUp(DdlFile));

        foreach (var slug in Slugs)
            (await ValorAsync(cn, slug)).Should().Be("true", slug);
        (await ValorAsync(cn, "reportes.detallados.read")).Should().Be("false");
    }

    [PostgresFact]
    public async Task Ddl_EsIdempotente()
    {
        await using var cn = await Fixture.OpenConnectionAsync();
        await SeedAsync(cn);
        await ExecAsync(cn, EmbeddedDdl.LoadUp(DdlFile));
        await ExecAsync(cn, EmbeddedDdl.LoadUp(DdlFile));

        foreach (var slug in Slugs)
            (await ValorAsync(cn, slug)).Should().Be("true", slug);
    }

    [PostgresFact]
    public async Task Down_DevuelveLosOchoSlugsAFalse()
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

        foreach (var slug in Slugs)
            (await ValorAsync(cn, slug)).Should().Be("false", slug);
    }
}
