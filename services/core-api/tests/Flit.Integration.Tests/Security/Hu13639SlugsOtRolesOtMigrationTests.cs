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
/// HU #13639 (Feature #13100 FA-3, Épica #12753) contra PostgreSQL real: el DDL 138 concede los slugs OT 1-4 del Anexo A
/// a todo rol <c>TRANSIT_OFFICE</c> que no es ot_admin (global y por tenant), sin tocar a los demás, de forma idempotente
/// y reversible. El DDL 137 se reaplica antes porque el arnés trunca security.modules/permissions entre pruebas.
/// <para>Uso de ejemplo: <c>await RunAsync(cn, "137-HU13638-slugs-ot.sql"); await RunAsync(cn, DdlFile);</c></para>
/// </summary>
public sealed class Hu13639SlugsOtRolesOtMigrationTests(PostgresDatabaseFixture fixture) : PostgresTestBase(fixture)
{
    private const string DdlFile = "138-HU13639-slugs-ot-roles-ot.sql";
    private const string MigrationId = "20261016170000_HU13639_SlugsOtRolesOt";
    private const string Tenant = "13639000-0000-4000-8000-000000000001";

    private static readonly string[] Dock =
        ["ot.tramites.manage", "ot.improntas.manage", "ot.reportes.manage", "ot.mandatos.manage"];

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

    private static async Task<long> CountAsync(NpgsqlConnection cn, string sql)
    {
        await using var cmd = new NpgsqlCommand(sql, cn);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync(TestContext.Current.CancellationToken));
    }

    private static Task RunAsync(NpgsqlConnection cn, string file) => ExecAsync(cn, EmbeddedDdl.LoadUp(file));

    // Roles: ot_admin, gestor global, gestor personalizado por tenant, gestor borrado, AdminCompany (COMPANY) y un rol COMPANY.
    private static async Task SeedAsync(NpgsqlConnection cn)
    {
        await ExecAsync(cn, $"""
            INSERT INTO identity.tenants (id, legal_name, tax_id, tenant_type, is_active, created_at)
            VALUES ('{Tenant}', 'OT 13639', '900136390-1', 'RENTING', true, now()) ON CONFLICT (id) DO NOTHING;
            INSERT INTO security.roles (code, name, is_system, is_active, target_entity_type, product_code, tenant_id, deleted_at)
            VALUES ('ot_admin', 'Administrador OT', true, true, 'TRANSIT_OFFICE', 'tramites', NULL, NULL),
                   ('gestor_tramites_ot', 'Operador OT', false, true, 'TRANSIT_OFFICE', 'tramites', NULL, NULL),
                   ('gestor_propio', 'Gestor propio', false, true, 'TRANSIT_OFFICE', 'tramites', '{Tenant}', NULL),
                   ('gestor_borrado', 'Gestor borrado', false, true, 'TRANSIT_OFFICE', 'tramites', NULL, now()),
                   ('AdminCompany', 'AdminCompany', true, true, 'COMPANY', 'plataforma', NULL, NULL);
            """);
        await RunAsync(cn, "137-HU13638-slugs-ot.sql");
    }

    private static Task<long> GrantsAsync(NpgsqlConnection cn, string roleCode, string slugFilter = "p.slug LIKE 'ot.%'") =>
        CountAsync(cn, $"""
            SELECT count(*) FROM security.role_permissions rp
              JOIN security.roles r ON r.id = rp.role_id
              JOIN security.permissions p ON p.id = rp.permission_id
             WHERE r.code = '{roleCode}' AND {slugFilter}
            """);

    [PostgresFact]
    public async Task Migracion_SeDescubreConAtributosInline()
    {
        await using var ctx = NewContext();
        ctx.Database.GetMigrations().Should().Contain(MigrationId);
        (await ctx.Database.GetAppliedMigrationsAsync(TestContext.Current.CancellationToken)).Should().Contain(MigrationId);
    }

    // AC3: roles OT no-admin (global y por tenant) reciben exactamente los slugs 1-4; nadie más.
    [PostgresFact]
    public async Task AC3_RolesOtNoAdmin_RecibenLosSlugsDelDock_GlobalesYPorTenant()
    {
        await using var cn = await Fixture.OpenConnectionAsync();
        await SeedAsync(cn);
        await RunAsync(cn, DdlFile);

        foreach (var rol in new[] { "gestor_tramites_ot", "gestor_propio" })
        {
            (await GrantsAsync(cn, rol)).Should().Be(4, rol);
            foreach (var slug in Dock)
            {
                (await GrantsAsync(cn, rol, $"p.slug = '{slug}'")).Should().Be(1, $"{rol} / {slug}");
            }

            // P1/P6: ni mandatarios, documentos, usuarios ni integración.
            (await GrantsAsync(cn, rol, "p.slug IN ('ot.mandatarios.manage','ot.documentos.manage','ot.usuarios.manage','ot.integracion.manage')"))
                .Should().Be(0, rol);
        }

        (await GrantsAsync(cn, "gestor_borrado")).Should().Be(0, "un rol con borrado lógico no recibe grants");
        (await GrantsAsync(cn, "AdminCompany")).Should().Be(0, "los roles COMPANY/plataforma no reciben slugs OT");
        (await GrantsAsync(cn, "ot_admin")).Should().Be(8, "ot_admin conserva los 8 del DDL 137, sin cambios");
    }

    // Idempotencia
    [PostgresFact]
    public async Task Ddl_EsIdempotente_SegundaEjecucionNoDuplica()
    {
        await using var cn = await Fixture.OpenConnectionAsync();
        await SeedAsync(cn);
        await RunAsync(cn, DdlFile);
        var primero = await CountAsync(cn, "SELECT count(*) FROM security.role_permissions");

        await RunAsync(cn, DdlFile);

        (await CountAsync(cn, "SELECT count(*) FROM security.role_permissions")).Should().Be(primero);
    }

    // Down reversible: quita los grants del backfill y deja los de ot_admin.
    [PostgresFact]
    public async Task Down_QuitaLosGrantsDelBackfill_YDejaAOtAdmin()
    {
        await using var cn = await Fixture.OpenConnectionAsync();
        await SeedAsync(cn);
        await RunAsync(cn, DdlFile);

        _downgraded = true;
        await using (var ctx = NewContext())
        {
            var all = ctx.Database.GetMigrations().ToList();
            await ctx.GetService<IMigrator>().MigrateAsync(all[all.IndexOf(MigrationId) - 1], TestContext.Current.CancellationToken);
        }

        (await GrantsAsync(cn, "gestor_tramites_ot")).Should().Be(0);
        (await GrantsAsync(cn, "gestor_propio")).Should().Be(0);
        (await GrantsAsync(cn, "ot_admin")).Should().Be(8);
    }
}
