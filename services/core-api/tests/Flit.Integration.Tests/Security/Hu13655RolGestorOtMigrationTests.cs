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
/// HU #13655 (Feature #13104 F-D2, Épica #12753) contra PostgreSQL real: el DDL 147 crea el rol de sistema «Gestor OT»
/// (<c>gestor_tramites_ot</c>, TRANSIT_OFFICE, trámites) con <c>ot.tramites.manage</c> y <c>ot.reportes.manage</c> y nada
/// más; es idempotente, respeta un rol ya existente (lo renombra sin quitarle permisos), no choca con un rol propio de un
/// organismo con el mismo código y su Down lo quita solo si nadie lo usa. El DDL 137 se reaplica antes porque el arnés
/// trunca security.modules/permissions entre pruebas.
/// </summary>
public sealed class Hu13655RolGestorOtMigrationTests(PostgresDatabaseFixture fixture) : PostgresTestBase(fixture)
{
    private const string DdlFile = "147-HU13655-rol-gestor-ot.sql";
    private const string MigrationId = "20261021120000_HU13655_RolGestorOt";
    private const string Tenant = "13655000-0000-4000-8000-000000000001";

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

    private static async Task<object?> ScalarAsync(NpgsqlConnection cn, string sql)
    {
        await using var cmd = new NpgsqlCommand(sql, cn);
        return await cmd.ExecuteScalarAsync(TestContext.Current.CancellationToken);
    }

    private static async Task<long> CountAsync(NpgsqlConnection cn, string sql) => Convert.ToInt64(await ScalarAsync(cn, sql));

    private static Task RunAsync(NpgsqlConnection cn, string file) => ExecAsync(cn, EmbeddedDdl.LoadUp(file));

    private static async Task SeedCatalogAsync(NpgsqlConnection cn)
    {
        await ExecAsync(cn, """
            INSERT INTO security.roles (code, name, is_system, is_active, target_entity_type, product_code, tenant_id)
            VALUES ('ot_admin', 'Administrador OT', true, true, 'TRANSIT_OFFICE', 'tramites', NULL);
            """);
        await RunAsync(cn, "137-HU13638-slugs-ot.sql");
    }

    private static Task<List<string>> SlugsAsync(NpgsqlConnection cn) => ListAsync(cn, """
        SELECT p.slug FROM security.role_permissions rp
          JOIN security.roles r ON r.id = rp.role_id
          JOIN security.permissions p ON p.id = rp.permission_id
         WHERE r.code = 'gestor_tramites_ot' AND r.tenant_id IS NULL AND r.deleted_at IS NULL
         ORDER BY p.slug
        """);

    private static async Task<List<string>> ListAsync(NpgsqlConnection cn, string sql)
    {
        await using var cmd = new NpgsqlCommand(sql, cn);
        await using var reader = await cmd.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        var list = new List<string>();
        while (await reader.ReadAsync(TestContext.Current.CancellationToken))
        {
            list.Add(reader.GetString(0));
        }

        return list;
    }

    [PostgresFact]
    public async Task Migracion_SeDescubreConAtributosInline_YEstaAplicada()
    {
        await using var ctx = NewContext();
        ctx.Database.GetMigrations().Should().Contain(MigrationId);
        (await ctx.Database.GetAppliedMigrationsAsync(TestContext.Current.CancellationToken)).Should().Contain(MigrationId);
    }

    [PostgresFact]
    public async Task Ddl_CreaElRolDeSistema_ConNombreVisibleYDescripcionSinCodigos()
    {
        await using var cn = await Fixture.OpenConnectionAsync();
        await SeedCatalogAsync(cn);

        await RunAsync(cn, DdlFile);

        (await CountAsync(cn, """
            SELECT count(*) FROM security.roles
             WHERE code = 'gestor_tramites_ot' AND name = 'Gestor OT' AND is_system AND is_active
               AND target_entity_type = 'TRANSIT_OFFICE' AND product_code = 'tramites'
               AND tenant_id IS NULL AND deleted_at IS NULL
            """)).Should().Be(1);
        var descripcion = (string)(await ScalarAsync(cn, "SELECT description FROM security.roles WHERE code = 'gestor_tramites_ot'"))!;
        descripcion.Should().NotBeNullOrWhiteSpace();
        descripcion.Should().NotContainAny("HU", "#", "gestor_tramites_ot", "ot_admin", "ot.");
    }

    [PostgresFact]
    public async Task Ddl_PermisosBase_SoloBandejaEInicio_SinUsuariosMandatariosPrelacionNiIntegracion()
    {
        await using var cn = await Fixture.OpenConnectionAsync();
        await SeedCatalogAsync(cn);

        await RunAsync(cn, DdlFile);

        (await SlugsAsync(cn)).Should().Equal("ot.reportes.manage", "ot.tramites.manage");
        (await CountAsync(cn, """
            SELECT count(*) FROM security.role_permissions rp
              JOIN security.roles r ON r.id = rp.role_id WHERE r.code = 'ot_admin'
            """)).Should().Be(8, "ot_admin conserva sus 8 slugs del DDL 137");
    }

    [PostgresFact]
    public async Task Ddl_EsIdempotente_SegundaEjecucionNoDuplica()
    {
        await using var cn = await Fixture.OpenConnectionAsync();
        await SeedCatalogAsync(cn);
        await RunAsync(cn, DdlFile);
        var roles = await CountAsync(cn, "SELECT count(*) FROM security.roles");
        var grants = await CountAsync(cn, "SELECT count(*) FROM security.role_permissions");

        await RunAsync(cn, DdlFile);

        (await CountAsync(cn, "SELECT count(*) FROM security.roles")).Should().Be(roles);
        (await CountAsync(cn, "SELECT count(*) FROM security.role_permissions")).Should().Be(grants);
    }

    // Coherencia con el DDL 138: si el rol ya existía (p. ej. «Operador OT» creado a mano y con los 4 slugs del backfill),
    // se renombra y queda de sistema, sin quitarle nada.
    [PostgresFact]
    public async Task Ddl_RolYaExistente_SeRenombraYConservaSusPermisos()
    {
        await using var cn = await Fixture.OpenConnectionAsync();
        await SeedCatalogAsync(cn);
        await ExecAsync(cn, """
            INSERT INTO security.roles (code, name, is_system, is_active, target_entity_type, product_code, tenant_id)
            VALUES ('gestor_tramites_ot', 'Operador OT', false, true, 'TRANSIT_OFFICE', 'tramites', NULL);
            """);
        await RunAsync(cn, "138-HU13639-slugs-ot-roles-ot.sql");

        await RunAsync(cn, DdlFile);

        (await CountAsync(cn, "SELECT count(*) FROM security.roles WHERE code = 'gestor_tramites_ot'")).Should().Be(1);
        (await ScalarAsync(cn, "SELECT name FROM security.roles WHERE code = 'gestor_tramites_ot'")).Should().Be("Gestor OT");
        (await ScalarAsync(cn, "SELECT is_system FROM security.roles WHERE code = 'gestor_tramites_ot'")).Should().Be(true);
        (await SlugsAsync(cn)).Should().Equal(
            "ot.improntas.manage", "ot.mandatos.manage", "ot.reportes.manage", "ot.tramites.manage");
    }

    [PostgresFact]
    public async Task Ddl_RolPropioDeUnOrganismoConElMismoCodigo_NoCreaElGlobalNiFalla()
    {
        await using var cn = await Fixture.OpenConnectionAsync();
        await SeedCatalogAsync(cn);
        await ExecAsync(cn, $"""
            INSERT INTO identity.tenants (id, legal_name, tax_id, tenant_type, is_active, created_at)
            VALUES ('{Tenant}', 'OT 13655', '900136550-1', 'RENTING', true, now());
            INSERT INTO security.roles (code, name, is_system, is_active, target_entity_type, product_code, tenant_id)
            VALUES ('gestor_tramites_ot', 'Gestor propio', false, true, 'TRANSIT_OFFICE', 'tramites', '{Tenant}');
            """);

        await RunAsync(cn, DdlFile);

        (await CountAsync(cn, "SELECT count(*) FROM security.roles WHERE code = 'gestor_tramites_ot' AND tenant_id IS NULL"))
            .Should().Be(0);
        (await ScalarAsync(cn, "SELECT name FROM security.roles WHERE code = 'gestor_tramites_ot'")).Should().Be("Gestor propio");
    }

    [PostgresFact]
    public async Task Down_QuitaElRolSinUso_YConservaAOtAdmin()
    {
        await using var cn = await Fixture.OpenConnectionAsync();
        await SeedCatalogAsync(cn);
        await RunAsync(cn, DdlFile);

        _downgraded = true;
        await using (var ctx = NewContext())
        {
            var all = ctx.Database.GetMigrations().ToList();
            await ctx.GetService<IMigrator>().MigrateAsync(all[all.IndexOf(MigrationId) - 1], TestContext.Current.CancellationToken);
        }

        (await CountAsync(cn, "SELECT count(*) FROM security.roles WHERE code = 'gestor_tramites_ot'")).Should().Be(0);
        (await CountAsync(cn, "SELECT count(*) FROM security.roles WHERE code = 'ot_admin'")).Should().Be(1);
    }
}
