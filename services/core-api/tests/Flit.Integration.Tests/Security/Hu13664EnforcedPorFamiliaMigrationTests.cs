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
/// HU #13664 (Feature #13636 F-F, Épica #12753) contra PostgreSQL real: el DDL 146 deja los 6 slugs por familia con
/// enforced = 'true' (los creó el DDL 145 en 'false'), es idempotente y su Down los devuelve a 'false'.
/// <para>Uso de ejemplo: <c>await SeedAsync(cn); await RunAsync(cn); await EnforcedAsync(cn, "traspasos.read") == "true"</c></para>
/// </summary>
public sealed class Hu13664EnforcedPorFamiliaMigrationTests(PostgresDatabaseFixture fixture) : PostgresTestBase(fixture)
{
    private const string DdlFile = "146-HU13664-enforced-por-familia.sql";
    private const string MigrationId = "20261019160000_HU13664_EnforcedPorFamilia";

    private static readonly string[] Slugs =
        ["matriculas.read", "matriculas.create", "traspasos.read", "traspasos.create", "otros-tramites.read", "otros-tramites.create"];

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

    private static async Task<string> EnforcedAsync(NpgsqlConnection cn, string slug)
    {
        await using var cmd = new NpgsqlCommand($"SELECT enforced FROM security.permissions WHERE slug = '{slug}'", cn);
        return (string)(await cmd.ExecuteScalarAsync(TestContext.Current.CancellationToken))!;
    }

    // El arnés trunca el catálogo entre pruebas: se siembran los 6 slugs en 'false' como los deja el DDL 145.
    private static Task SeedAsync(NpgsqlConnection cn) => ExecAsync(cn, """
        INSERT INTO security.modules (code, name, sort_order, is_active, product_code)
        VALUES ('tramites-traspasos', 'Trámites: Traspasos', 90, true, 'tramites') ON CONFLICT DO NOTHING;
        INSERT INTO security.permissions (module_id, slug, name, http_method, route_pattern, is_active)
        SELECT m.id, v.slug, v.slug, 'GET', '/x', true
          FROM security.modules m,
               (VALUES ('matriculas.read'), ('matriculas.create'), ('traspasos.read'), ('traspasos.create'),
                       ('otros-tramites.read'), ('otros-tramites.create')) AS v(slug)
         WHERE m.code = 'tramites-traspasos'
        ON CONFLICT DO NOTHING;
        """);

    [PostgresFact]
    public async Task Migracion_SeDescubreConAtributosInline()
    {
        await using var ctx = NewContext();
        ctx.Database.GetMigrations().Should().Contain(MigrationId);
        (await ctx.Database.GetAppliedMigrationsAsync(TestContext.Current.CancellationToken)).Should().Contain(MigrationId);
    }

    [PostgresFact]
    public async Task Up_MarcaLosSeisSlugsEnforcedTrue_YEsIdempotente()
    {
        await using var cn = await Fixture.OpenConnectionAsync();
        await SeedAsync(cn);
        foreach (var slug in Slugs)
            (await EnforcedAsync(cn, slug)).Should().Be("false", slug);

        await ExecAsync(cn, EmbeddedDdl.LoadUp(DdlFile));
        await ExecAsync(cn, EmbeddedDdl.LoadUp(DdlFile));

        foreach (var slug in Slugs)
            (await EnforcedAsync(cn, slug)).Should().Be("true", slug);
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

        foreach (var slug in Slugs)
            (await EnforcedAsync(cn, slug)).Should().Be("false", slug);
    }
}
