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
/// HU #13641 (Feature #13100 FA-6, Épica #12753) contra PostgreSQL real: <c>security.permissions.enforced</c> existe NOT NULL con
/// CHECK (true/false/excluded), el DDL 139 clasifica los slugs (AC3 excluidos, AC4 los 8 OT en true), es idempotente y su Down
/// quita la columna. El arnés trunca security.permissions entre pruebas, así que cada prueba siembra los slugs que necesita.
/// <para>Uso de ejemplo: <c>await SeedAsync(cn); await RunAsync(cn, DdlFile); await ValorAsync(cn, "banners.manage") == "excluded"</c></para>
/// </summary>
public sealed class Hu13641PermisosEnforcedMigrationTests(PostgresDatabaseFixture fixture) : PostgresTestBase(fixture)
{
    private const string DdlFile = "139-HU13641-permisos-enforced.sql";
    private const string MigrationId = "20261016190000_HU13641_PermisosEnforced";

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

    // Los 8 OT llegan por el DDL 137; los demás se insertan en un módulo propio.
    private static async Task SeedAsync(NpgsqlConnection cn)
    {
        await ExecAsync(cn, """
            INSERT INTO security.modules (code, name, sort_order, is_active, product_code)
            VALUES ('m13641', 'Módulo 13641', 9999, true, 'plataforma') ON CONFLICT DO NOTHING;
            INSERT INTO security.permissions (module_id, slug, name, http_method, route_pattern, is_active)
            SELECT m.id, s, s, 'GET', '/x', true
              FROM security.modules m, unnest(ARRAY['usuarios.manage','rbac.manage','auth.me.read','banners.manage','tramites.read','reportes.read','slug.nuevo']) s
             WHERE m.code = 'm13641';
            """);
        await ExecAsync(cn, EmbeddedDdl.LoadUp("137-HU13638-slugs-ot.sql"));
    }

    [PostgresFact]
    public async Task Migracion_SeDescubreConAtributosInline()
    {
        await using var ctx = NewContext();
        ctx.Database.GetMigrations().Should().Contain(MigrationId);
        (await ctx.Database.GetAppliedMigrationsAsync(TestContext.Current.CancellationToken)).Should().Contain(MigrationId);
    }

    // AC1: columna NOT NULL; un slug sin clasificar nace 'false'; el CHECK rechaza otros valores.
    [PostgresFact]
    public async Task AC1_ColumnaNotNullConCheck_YDefaultFalse()
    {
        await using var cn = await Fixture.OpenConnectionAsync();
        await SeedAsync(cn);
        await ExecAsync(cn, EmbeddedDdl.LoadUp(DdlFile));

        await using var cmd = new NpgsqlCommand(
            "SELECT is_nullable FROM information_schema.columns WHERE table_schema='security' AND table_name='permissions' AND column_name='enforced'", cn);
        (await cmd.ExecuteScalarAsync(TestContext.Current.CancellationToken)).Should().Be("NO");
        (await ValorAsync(cn, "slug.nuevo")).Should().Be("false");

        var act = () => ExecAsync(cn, "UPDATE security.permissions SET enforced = 'quizas' WHERE slug = 'slug.nuevo'");
        await act.Should().ThrowAsync<PostgresException>();
    }

    // AC3 y AC4
    [PostgresFact]
    public async Task AC3_AC4_ExcluidosYOtQuedanClasificados()
    {
        await using var cn = await Fixture.OpenConnectionAsync();
        await SeedAsync(cn);
        await ExecAsync(cn, EmbeddedDdl.LoadUp(DdlFile));

        foreach (var slug in new[] { "usuarios.manage", "rbac.manage", "auth.me.read", "banners.manage" })
            (await ValorAsync(cn, slug)).Should().Be("excluded", slug);
        foreach (var slug in new[] { "tramites", "improntas", "reportes", "mandatos", "mandatarios", "documentos", "usuarios", "integracion" })
            (await ValorAsync(cn, $"ot.{slug}.manage")).Should().Be("true", slug);
        (await ValorAsync(cn, "tramites.read")).Should().Be("true");
        (await ValorAsync(cn, "reportes.read")).Should().Be("false");
    }

    [PostgresFact]
    public async Task Ddl_EsIdempotente()
    {
        await using var cn = await Fixture.OpenConnectionAsync();
        await SeedAsync(cn);
        await ExecAsync(cn, EmbeddedDdl.LoadUp(DdlFile));
        await ExecAsync(cn, EmbeddedDdl.LoadUp(DdlFile));

        (await ValorAsync(cn, "ot.tramites.manage")).Should().Be("true");
        (await ValorAsync(cn, "banners.manage")).Should().Be("excluded");
    }

    [PostgresFact]
    public async Task Down_QuitaLaColumna()
    {
        await using var cn = await Fixture.OpenConnectionAsync();
        _downgraded = true;
        await using (var ctx = NewContext())
        {
            var all = ctx.Database.GetMigrations().ToList();
            await ctx.GetService<IMigrator>().MigrateAsync(all[all.IndexOf(MigrationId) - 1], TestContext.Current.CancellationToken);
        }

        await using var cmd = new NpgsqlCommand(
            "SELECT count(*) FROM information_schema.columns WHERE table_schema='security' AND table_name='permissions' AND column_name='enforced'", cn);
        Convert.ToInt64(await cmd.ExecuteScalarAsync(TestContext.Current.CancellationToken)).Should().Be(0);
    }
}
