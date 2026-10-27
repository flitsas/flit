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
/// HU #13663 (Feature #13636 F-F, Épica #12753) contra PostgreSQL real: el DDL 145 crea los 6 slugs Ver/Crear por familia
/// (producto tramites), los backfillea desde tramites.read / tramites.create (rol global y por tenant), es idempotente y
/// su Down lo revierte. Nacen con enforced = 'false'; los pasa a 'true' la HU #13664 (DDL 146).
/// <para>Uso de ejemplo: <c>await SeedAsync(cn); await RunAsync(cn); await TieneAsync(cn, "rol_propio", "traspasos.read") == 1</c></para>
/// </summary>
public sealed class Hu13663PermisosPorFamiliaMigrationTests(PostgresDatabaseFixture fixture) : PostgresTestBase(fixture)
{
    private const string DdlFile = "145-HU13663-permisos-por-familia.sql";
    private const string MigrationId = "20261019150000_HU13663_PermisosPorFamilia";
    private const string Tenant = "13663000-0000-4000-8000-000000000001";
    private const string FamilySlugRegex = "^(matriculas|traspasos|otros-tramites)\\.";

    private static readonly (string Slug, string Module)[] Expected =
    [
        ("matriculas.read", "tramites-matriculas"), ("matriculas.create", "tramites-matriculas"),
        ("traspasos.read", "tramites-traspasos"), ("traspasos.create", "tramites-traspasos"),
        ("otros-tramites.read", "tramites-otros"), ("otros-tramites.create", "tramites-otros"),
    ];

    private static readonly string[] Reads = ["matriculas.read", "traspasos.read", "otros-tramites.read"];
    private static readonly string[] Creates = ["matriculas.create", "traspasos.create", "otros-tramites.create"];

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

    private static Task<long> TieneAsync(NpgsqlConnection cn, string roleCode, string slug) =>
        CountAsync(cn, $"""
            SELECT count(*) FROM security.role_permissions rp
              JOIN security.roles r ON r.id = rp.role_id
              JOIN security.permissions p ON p.id = rp.permission_id
             WHERE r.code = '{roleCode}' AND p.slug = '{slug}'
            """);

    // Roles de producto tramites: global solo-lectura, global lector+creador, por tenant (lee+crea), sin permisos, borrado.
    private static async Task SeedAsync(NpgsqlConnection cn)
    {
        await ExecAsync(cn, $"""
            INSERT INTO identity.tenants (id, legal_name, tax_id, tenant_type, is_active, created_at)
            VALUES ('{Tenant}', 'Compañía 13663', '900136630-1', 'RENTING', true, now()) ON CONFLICT (id) DO NOTHING;
            INSERT INTO security.roles (code, name, is_system, is_active, target_entity_type, product_code, tenant_id, deleted_at)
            VALUES ('solo_lectura', 'Solo lectura', false, true, 'COMPANY', 'tramites', NULL, NULL),
                   ('Radicador', 'Radicador', false, true, 'COMPANY', 'tramites', NULL, NULL),
                   ('rol_propio', 'Rol propio', false, true, 'COMPANY', 'tramites', '{Tenant}', NULL),
                   ('sin_permisos', 'Sin permisos', false, true, 'COMPANY', 'tramites', NULL, NULL),
                   ('rol_borrado', 'Rol borrado', false, true, 'COMPANY', 'tramites', NULL, now())
            ON CONFLICT DO NOTHING;
            INSERT INTO security.modules (code, name, sort_order, is_active, product_code)
            VALUES ('tramites', 'Trámites', 2, true, 'tramites') ON CONFLICT DO NOTHING;
            INSERT INTO security.permissions (module_id, slug, name, http_method, route_pattern, is_active)
            SELECT m.id, v.slug, v.name, v.method, '/api/v1/tramites', true
              FROM security.modules m,
                   (VALUES ('tramites.read', 'Ver trámites', 'GET'), ('tramites.create', 'Crear trámite', 'POST')) AS v(slug, name, method)
             WHERE m.code = 'tramites' ON CONFLICT DO NOTHING;
            INSERT INTO security.role_permissions (role_id, permission_id)
            SELECT r.id, p.id FROM security.roles r, security.permissions p
             WHERE (r.code = 'solo_lectura' AND p.slug = 'tramites.read')
                OR (r.code IN ('Radicador', 'rol_propio', 'rol_borrado') AND p.slug IN ('tramites.read', 'tramites.create'))
            ON CONFLICT DO NOTHING;
            """);
    }

    [PostgresFact]
    public async Task Migracion_SeDescubreConAtributosInline()
    {
        await using var ctx = NewContext();
        ctx.Database.GetMigrations().Should().Contain(MigrationId);
        (await ctx.Database.GetAppliedMigrationsAsync(TestContext.Current.CancellationToken)).Should().Contain(MigrationId);
    }

    // AC1: catálogo por familia, producto tramites. enforced queda 'false' hasta que la HU #13664 los exija.
    [PostgresFact]
    public async Task AC1_Catalogo_CreaLosSeisSlugsConModuloProductoYEnforcedFalse()
    {
        await using var cn = await Fixture.OpenConnectionAsync();
        await SeedAsync(cn);
        await RunAsync(cn);

        foreach (var (slug, module) in Expected)
            (await CountAsync(cn, $"""
                SELECT count(*) FROM security.permissions p JOIN security.modules m ON m.id = p.module_id
                 WHERE p.slug = '{slug}' AND m.code = '{module}' AND m.product_code = 'tramites' AND p.is_active AND p.enforced = 'false'
                """)).Should().Be(1, slug);
    }

    // AC2: sin regresión — quien tenía tramites.read/create recibe los .read/.create de las tres familias.
    [PostgresFact]
    public async Task AC2_Backfill_DesdeTramitesReadYCreate_RolGlobalYPorTenant()
    {
        await using var cn = await Fixture.OpenConnectionAsync();
        await SeedAsync(cn);
        await RunAsync(cn);

        foreach (var rol in new[] { "solo_lectura", "Radicador", "rol_propio" })
            foreach (var slug in Reads)
                (await TieneAsync(cn, rol, slug)).Should().Be(1, $"{rol} {slug}");
        foreach (var rol in new[] { "Radicador", "rol_propio" })
            foreach (var slug in Creates)
                (await TieneAsync(cn, rol, slug)).Should().Be(1, $"{rol} {slug}");
        foreach (var slug in Creates)
            (await TieneAsync(cn, "solo_lectura", slug)).Should().Be(0, $"solo_lectura no crea: {slug}");
        foreach (var slug in Reads.Concat(Creates))
        {
            (await TieneAsync(cn, "sin_permisos", slug)).Should().Be(0, slug);
            (await TieneAsync(cn, "rol_borrado", slug)).Should().Be(0, slug);
        }
    }

    // AC3: idempotente.
    [PostgresFact]
    public async Task AC3_Ddl_EsIdempotente()
    {
        await using var cn = await Fixture.OpenConnectionAsync();
        await SeedAsync(cn);
        await RunAsync(cn);
        var grants = await CountAsync(cn, "SELECT count(*) FROM security.role_permissions");
        var perms = await CountAsync(cn, "SELECT count(*) FROM security.permissions");
        var mods = await CountAsync(cn, "SELECT count(*) FROM security.modules");
        await RunAsync(cn);

        (await CountAsync(cn, "SELECT count(*) FROM security.role_permissions")).Should().Be(grants);
        (await CountAsync(cn, "SELECT count(*) FROM security.permissions")).Should().Be(perms);
        (await CountAsync(cn, "SELECT count(*) FROM security.modules")).Should().Be(mods);
        // 3 roles x 3 .read + 2 roles x 3 .create
        (await CountAsync(cn, $"SELECT count(*) FROM security.role_permissions rp JOIN security.permissions p ON p.id = rp.permission_id WHERE p.slug ~ '{FamilySlugRegex}'"))
            .Should().Be(15);
    }

    [PostgresFact]
    public async Task Down_EliminaCatalogoYGrantsDeLasFamilias()
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

        (await CountAsync(cn, $"SELECT count(*) FROM security.permissions WHERE slug ~ '{FamilySlugRegex}'")).Should().Be(0);
        (await CountAsync(cn, "SELECT count(*) FROM security.modules WHERE code IN ('tramites-matriculas','tramites-traspasos','tramites-otros')")).Should().Be(0);
        (await TieneAsync(cn, "Radicador", "tramites.create")).Should().Be(1, "los slugs tramites.* no se tocan");
    }
}
