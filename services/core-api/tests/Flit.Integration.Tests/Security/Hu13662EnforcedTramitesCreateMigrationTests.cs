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
/// HU #13662 (Feature #13105 F-E, Épica #12753) contra PostgreSQL real: el DDL 143 concede <c>tramites.create</c> a todo rol
/// activo de producto <c>tramites</c> (hoy ya crea trámites), no toca roles de plataforma ni borrados, marca el slug
/// enforced = true, es idempotente y su Down lo devuelve a 'false'.
/// <para>Uso de ejemplo: <c>await SeedAsync(cn); await RunAsync(cn); await TieneAsync(cn, "ot_admin") == 1</c></para>
/// </summary>
public sealed class Hu13662EnforcedTramitesCreateMigrationTests(PostgresDatabaseFixture fixture) : PostgresTestBase(fixture)
{
    private const string DdlFile = "143-HU13662-enforced-tramites-create.sql";
    private const string MigrationId = "20261019130000_HU13662_EnforcedTramitesCreate";
    private const string Tenant = "13662000-0000-4000-8000-000000000001";

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

    private static Task RunAsync(NpgsqlConnection cn) => ExecAsync(cn, EmbeddedDdl.LoadUp(DdlFile));

    private static Task<long> TieneAsync(NpgsqlConnection cn, string roleCode) =>
        CountAsync(cn, $"""
            SELECT count(*) FROM security.role_permissions rp
              JOIN security.roles r ON r.id = rp.role_id
              JOIN security.permissions p ON p.id = rp.permission_id
             WHERE r.code = '{roleCode}' AND p.slug = 'tramites.create'
            """);

    private static async Task<string> EnforcedAsync(NpgsqlConnection cn)
    {
        await using var cmd = new NpgsqlCommand("SELECT enforced FROM security.permissions WHERE slug = 'tramites.create'", cn);
        return (string)(await cmd.ExecuteScalarAsync(TestContext.Current.CancellationToken))!;
    }

    // Roles: catálogo global (OT y compañía), uno por tenant, uno borrado, uno inactivo, y los de plataforma.
    // Radicador ya tiene el slug (el backfill no debe duplicarlo).
    private static async Task SeedAsync(NpgsqlConnection cn)
    {
        await ExecAsync(cn, $"""
            INSERT INTO identity.tenants (id, legal_name, tax_id, tenant_type, is_active, created_at)
            VALUES ('{Tenant}', 'Compañía 13662', '900136620-1', 'RENTING', true, now()) ON CONFLICT (id) DO NOTHING;
            INSERT INTO security.roles (code, name, is_system, is_active, target_entity_type, product_code, tenant_id, deleted_at)
            VALUES ('admin_tramites', 'Admin trámites', true, true, 'COMPANY', 'tramites', NULL, NULL),
                   ('Radicador', 'Radicador', false, true, 'COMPANY', 'tramites', NULL, NULL),
                   ('ot_admin', 'Administrador OT', true, true, 'TRANSIT_OFFICE', 'tramites', NULL, NULL),
                   ('gestor_tramites_ot', 'Operador OT', false, true, 'TRANSIT_OFFICE', 'tramites', NULL, NULL),
                   ('rol_propio', 'Rol propio', false, true, 'COMPANY', 'tramites', '{Tenant}', NULL),
                   ('rol_borrado', 'Rol borrado', false, true, 'COMPANY', 'tramites', NULL, now()),
                   ('rol_inactivo', 'Rol inactivo', false, false, 'COMPANY', 'tramites', NULL, NULL),
                   ('SuperAdmin', 'SuperAdmin', true, true, 'COMPANY', 'plataforma', NULL, NULL),
                   ('AdminCompany', 'AdminCompany', true, true, 'COMPANY', 'plataforma', NULL, NULL)
            ON CONFLICT DO NOTHING;
            INSERT INTO security.modules (code, name, sort_order, is_active, product_code)
            VALUES ('tramites', 'Trámites', 2, true, 'tramites') ON CONFLICT DO NOTHING;
            INSERT INTO security.permissions (module_id, slug, name, http_method, route_pattern, is_active)
            SELECT m.id, 'tramites.create', 'Crear trámite', 'POST', '/api/v1/tramites', true
              FROM security.modules m WHERE m.code = 'tramites' ON CONFLICT DO NOTHING;
            INSERT INTO security.role_permissions (role_id, permission_id)
            SELECT r.id, p.id FROM security.roles r, security.permissions p
             WHERE r.code = 'Radicador' AND p.slug = 'tramites.create' ON CONFLICT DO NOTHING;
            """);
    }

    [PostgresFact]
    public async Task Migracion_SeDescubreConAtributosInline()
    {
        await using var ctx = NewContext();
        ctx.Database.GetMigrations().Should().Contain(MigrationId);
        (await ctx.Database.GetAppliedMigrationsAsync(TestContext.Current.CancellationToken)).Should().Contain(MigrationId);
    }

    // AC1: quien ya tenía tramites.create (los roles que la UI dejaba crear) lo conserva; la migración no se lo da a nadie más
    // (decisión 2026-10-19: sin backfill, para no autorizar a crear a roles de solo lectura).
    [PostgresFact]
    public async Task AC1_Up_ConservaElSlugDeQuienLoTenia_SinBackfill_YMarcaEnforced()
    {
        await using var cn = await Fixture.OpenConnectionAsync();
        await SeedAsync(cn);
        await RunAsync(cn);

        (await TieneAsync(cn, "Radicador")).Should().Be(1, "lo tenía antes de la migración");
        foreach (var rol in new[] { "admin_tramites", "ot_admin", "gestor_tramites_ot", "rol_propio", "rol_borrado", "rol_inactivo", "SuperAdmin", "AdminCompany" })
            (await TieneAsync(cn, rol)).Should().Be(0, rol);
        // AC3
        (await EnforcedAsync(cn)).Should().Be("true");
    }

    [PostgresFact]
    public async Task Ddl_EsIdempotente()
    {
        await using var cn = await Fixture.OpenConnectionAsync();
        await SeedAsync(cn);
        await RunAsync(cn);
        var primero = await CountAsync(cn, "SELECT count(*) FROM security.role_permissions");
        await RunAsync(cn);

        (await CountAsync(cn, "SELECT count(*) FROM security.role_permissions")).Should().Be(primero);
        (await EnforcedAsync(cn)).Should().Be("true");
    }

    [PostgresFact]
    public async Task Down_DevuelveElMarcadorAFalse_YConservaLosGrants()
    {
        await using var cn = await Fixture.OpenConnectionAsync();
        await SeedAsync(cn);
        await RunAsync(cn);
        _downgraded = true;
        await using (var ctx = NewContext())
        {
            var all = ctx.Database.GetMigrations().ToList();
            await ctx.GetService<IMigrator>().MigrateAsync(all[all.IndexOf(MigrationId) - 1], TestContext.Current.CancellationToken);
        }

        (await EnforcedAsync(cn)).Should().Be("false");
        (await TieneAsync(cn, "Radicador")).Should().Be(1, "el Down solo devuelve el marcador; los grants previos se conservan");
    }
}
