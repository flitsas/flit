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
/// HU #13661 (Feature #13105 F-E, Épica #12753) contra PostgreSQL real: el DDL 142 marca <c>improntas.read</c> e
/// <c>improntas.generate</c> como <c>enforced = 'true'</c> (AC3), es idempotente y su Down los devuelve a 'false'.
/// <para>Uso de ejemplo: <c>await SeedAsync(cn); await ExecAsync(cn, EmbeddedDdl.LoadUp(DdlFile)); await ValorAsync(cn, "improntas.read") == "true"</c></para>
/// </summary>
public sealed class Hu13661EnforcedImprontasMigrationTests(PostgresDatabaseFixture fixture) : PostgresTestBase(fixture)
{
    private const string DdlFile = "142-HU13661-enforced-improntas.sql";
    private const string MigrationId = "20261019120000_HU13661_EnforcedImprontas";

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

    // El arnés trunca security.permissions entre pruebas: se siembran los slugs (nacen 'false') en un módulo propio.
    private static Task SeedAsync(NpgsqlConnection cn) => ExecAsync(cn, """
        INSERT INTO security.modules (code, name, sort_order, is_active, product_code)
        VALUES ('m13661', 'Módulo 13661', 9999, true, 'plataforma') ON CONFLICT DO NOTHING;
        INSERT INTO security.permissions (module_id, slug, name, http_method, route_pattern, is_active)
        SELECT m.id, s, s, 'GET', '/x', true
          FROM security.modules m, unnest(ARRAY['improntas.read','improntas.generate','slug.otro']) s
         WHERE m.code = 'm13661';
        """);

    [PostgresFact]
    public async Task Migracion_SeDescubreConAtributosInline()
    {
        await using var ctx = NewContext();
        ctx.Database.GetMigrations().Should().Contain(MigrationId);
        (await ctx.Database.GetAppliedMigrationsAsync(TestContext.Current.CancellationToken)).Should().Contain(MigrationId);
    }

    [PostgresFact]
    public async Task Up_MarcaLosDosSlugsEnTrue_YNoTocaOtros()
    {
        await using var cn = await Fixture.OpenConnectionAsync();
        await SeedAsync(cn);
        (await ValorAsync(cn, "improntas.read")).Should().Be("false");

        await ExecAsync(cn, EmbeddedDdl.LoadUp(DdlFile));

        (await ValorAsync(cn, "improntas.read")).Should().Be("true");
        (await ValorAsync(cn, "improntas.generate")).Should().Be("true");
        (await ValorAsync(cn, "slug.otro")).Should().Be("false");
    }

    [PostgresFact]
    public async Task Ddl_EsIdempotente()
    {
        await using var cn = await Fixture.OpenConnectionAsync();
        await SeedAsync(cn);
        await ExecAsync(cn, EmbeddedDdl.LoadUp(DdlFile));
        await ExecAsync(cn, EmbeddedDdl.LoadUp(DdlFile));

        (await ValorAsync(cn, "improntas.read")).Should().Be("true");
        (await ValorAsync(cn, "improntas.generate")).Should().Be("true");
    }

    [PostgresFact]
    public async Task Down_DevuelveLosSlugsAFalse()
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

        (await ValorAsync(cn, "improntas.read")).Should().Be("false");
        (await ValorAsync(cn, "improntas.generate")).Should().Be("false");
    }
}
