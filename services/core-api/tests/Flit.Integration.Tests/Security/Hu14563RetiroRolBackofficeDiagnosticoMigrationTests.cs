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
/// HU #14563 (decisión D24 · L1) contra PostgreSQL real: el DDL 196 retira el rol global de producto
/// <c>backoffice_diagnostico</c>, que el manifiesto de Diagnóstico dejó de declarar (D23) y que el
/// <c>ProductManifestStore</c> no borra. Cierra sus asignaciones vigentes (soft delete), borra sus permisos y lo deja
/// inactivo y en soft delete, sin borrarlo físicamente. Es idempotente y no toca <c>admin_diagnostico</c>,
/// <c>usuario_diagnostico</c> ni un rol propio de una compañía con el mismo código. El Down lo restituye con lo que
/// cerró la migración, leyendo el rastro de <c>audit.audit_logs</c>.
/// </summary>
public sealed class Hu14563RetiroRolBackofficeDiagnosticoMigrationTests(PostgresDatabaseFixture fixture) : PostgresTestBase(fixture)
{
    private const string DdlFile = "196-HU14563-retiro-rol-backoffice-diagnostico.sql";
    private const string MigrationId = "20261113120000_HU14563_RetiroRolBackofficeDiagnostico";
    private const string Tenant = "14563000-0000-4000-8000-000000000001";
    private const string OtroTenant = "14563000-0000-4000-8000-000000000002";
    private const string UserBackoffice = "14563000-0000-4000-8000-0000000000a1";
    private const string UserAdmin = "14563000-0000-4000-8000-0000000000a2";
    private const string UserUsuario = "14563000-0000-4000-8000-0000000000a3";
    private const string UserPropio = "14563000-0000-4000-8000-0000000000a4";

    private const string RolGlobal =
        "code = 'backoffice_diagnostico' AND product_code = 'diagnostico' AND tenant_id IS NULL";

    private const string RolGlobalR =
        "r.code = 'backoffice_diagnostico' AND r.product_code = 'diagnostico' AND r.tenant_id IS NULL";

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

    private static Task RunAsync(NpgsqlConnection cn) => ExecAsync(cn, EmbeddedDdl.LoadUp(DdlFile));

    // Estado de DEV/QA/PDN tras aplicar el manifiesto de Diagnóstico anterior a D23: los tres roles globales con
    // permisos y una asignación cada uno, más un rol propio de otra compañía con el mismo código.
    private static async Task SeedAsync(NpgsqlConnection cn)
    {
        await ExecAsync(cn, $"""
            INSERT INTO identity.tenants (id, code, legal_name, tax_id, tenant_type, is_active, is_group_parent, created_at) VALUES
              ('{Tenant}','IT-14563-A','FLIT 14563','9145630000001','FLIT',true,false,now()),
              ('{OtroTenant}','IT-14563-B','Otra 14563','9145630000002','RENTING',true,false,now());
            INSERT INTO identity.users (id, email, display_name, status, home_tenant_id, created_at) VALUES
              ('{UserBackoffice}','bo14563@it.local','Bo','active','{Tenant}',now()),
              ('{UserAdmin}','ad14563@it.local','Ad','active','{Tenant}',now()),
              ('{UserUsuario}','us14563@it.local','Us','active','{Tenant}',now()),
              ('{UserPropio}','pr14563@it.local','Pr','active','{OtroTenant}',now());
            INSERT INTO security.modules (code, name, sort_order, is_active, product_code)
              VALUES ('it14563_dx','IT 14563',1,true,'diagnostico');
            INSERT INTO security.permissions (module_id, slug, name, http_method, route_pattern, is_active, enforced)
              SELECT m.id, v.slug, v.slug, 'ANY', '', true, 'true'
                FROM security.modules m
                JOIN (VALUES ('diagnostico.it14563.ver'), ('diagnostico.it14563.auditar')) AS v(slug) ON m.code = 'it14563_dx';
            INSERT INTO security.roles (code, name, is_system, is_active, target_entity_type, product_code, tenant_id) VALUES
              ('backoffice_diagnostico','Backoffice FLIT de Diagnóstico',true,true,'COMPANY','diagnostico',NULL),
              ('admin_diagnostico','Administrador de Diagnóstico',true,true,'COMPANY','diagnostico',NULL),
              ('usuario_diagnostico','Usuario de Diagnóstico',true,true,'COMPANY','diagnostico',NULL);
            INSERT INTO security.role_permissions (role_id, permission_id)
              SELECT r.id, p.id FROM security.roles r CROSS JOIN security.permissions p
               WHERE r.code IN ('backoffice_diagnostico','admin_diagnostico','usuario_diagnostico')
                 AND r.tenant_id IS NULL AND p.slug LIKE 'diagnostico.it14563.%';
            INSERT INTO security.user_role_assignments (tenant_id, user_id, role_id)
              SELECT '{Tenant}', v.user_id::uuid, r.id
                FROM (VALUES ('{UserBackoffice}','backoffice_diagnostico'), ('{UserAdmin}','admin_diagnostico'),
                             ('{UserUsuario}','usuario_diagnostico')) AS v(user_id, code)
                JOIN security.roles r ON r.code = v.code AND r.tenant_id IS NULL;
            """);
    }

    private static Task<long> PermisosDelRolGlobalAsync(NpgsqlConnection cn) => CountAsync(cn, $"""
        SELECT count(*) FROM security.role_permissions rp JOIN security.roles r ON r.id = rp.role_id WHERE {RolGlobalR}
        """);

    private static Task<long> AsignacionesVigentesAsync(NpgsqlConnection cn, string code) => CountAsync(cn, $"""
        SELECT count(*) FROM security.user_role_assignments a JOIN security.roles r ON r.id = a.role_id
         WHERE r.code = '{code}' AND r.tenant_id IS NULL AND a.deleted_at IS NULL
        """);

    [PostgresFact]
    public async Task Migracion_SeDescubreConAtributosInline_YEstaAplicada()
    {
        await using var ctx = NewContext();
        ctx.Database.GetMigrations().Should().Contain(MigrationId);
        (await ctx.Database.GetAppliedMigrationsAsync(TestContext.Current.CancellationToken)).Should().Contain(MigrationId);
    }

    [PostgresFact]
    public async Task Ddl_CierraAsignaciones_BorraPermisos_YDesactivaElRolSinBorrarlo()
    {
        await using var cn = await Fixture.OpenConnectionAsync();
        await SeedAsync(cn);
        (await PermisosDelRolGlobalAsync(cn)).Should().Be(2);
        (await AsignacionesVigentesAsync(cn, "backoffice_diagnostico")).Should().Be(1);

        await RunAsync(cn);

        (await CountAsync(cn, $"""
            SELECT count(*) FROM security.roles
             WHERE {RolGlobal} AND NOT is_active AND deleted_at IS NOT NULL AND updated_at IS NOT NULL
            """)).Should().Be(1, "el rol queda inactivo y en soft delete, sin borrado físico");
        (await PermisosDelRolGlobalAsync(cn)).Should().Be(0);
        (await AsignacionesVigentesAsync(cn, "backoffice_diagnostico")).Should().Be(0);
        (await CountAsync(cn, $"""
            SELECT count(*) FROM security.user_role_assignments a JOIN security.roles r ON r.id = a.role_id
             WHERE {RolGlobalR} AND a.deleted_at = r.deleted_at AND a.updated_at IS NOT NULL
            """)).Should().Be(1, "la asignación se cierra en la misma transacción que el rol");
    }

    [PostgresFact]
    public async Task Ddl_NoTocaLosOtrosRolesDeDiagnostico_NiElRolPropioDeUnaCompania()
    {
        await using var cn = await Fixture.OpenConnectionAsync();
        await SeedAsync(cn);
        await RunAsync(cn);

        // Con el global ya retirado, una compañía puede tener su propio rol con ese código (trigger del DDL 134).
        await ExecAsync(cn, $"""
            INSERT INTO security.roles (code, name, is_system, is_active, target_entity_type, product_code, tenant_id)
              VALUES ('backoffice_diagnostico','Backoffice propio',false,true,'COMPANY','diagnostico','{OtroTenant}');
            INSERT INTO security.role_permissions (role_id, permission_id)
              SELECT r.id, p.id FROM security.roles r CROSS JOIN security.permissions p
               WHERE r.code = 'backoffice_diagnostico' AND r.tenant_id = '{OtroTenant}' AND p.slug LIKE 'diagnostico.it14563.%';
            INSERT INTO security.user_role_assignments (tenant_id, user_id, role_id)
              SELECT '{OtroTenant}', '{UserPropio}', id FROM security.roles
               WHERE code = 'backoffice_diagnostico' AND tenant_id = '{OtroTenant}';
            """);

        await RunAsync(cn);

        foreach (var code in new[] { "admin_diagnostico", "usuario_diagnostico" })
        {
            (await CountAsync(cn, $"""
                SELECT count(*) FROM security.roles
                 WHERE code = '{code}' AND tenant_id IS NULL AND is_active AND deleted_at IS NULL
                """)).Should().Be(1, $"{code} sigue activo");
            (await CountAsync(cn, $"""
                SELECT count(*) FROM security.role_permissions rp JOIN security.roles r ON r.id = rp.role_id
                 WHERE r.code = '{code}' AND r.tenant_id IS NULL
                """)).Should().Be(2, $"{code} conserva sus permisos");
            (await AsignacionesVigentesAsync(cn, code)).Should().Be(1, $"{code} conserva su asignación");
        }

        (await CountAsync(cn, $"""
            SELECT count(*) FROM security.roles r
             WHERE r.code = 'backoffice_diagnostico' AND r.tenant_id = '{OtroTenant}' AND r.is_active AND r.deleted_at IS NULL
               AND (SELECT count(*) FROM security.role_permissions rp WHERE rp.role_id = r.id) = 2
               AND EXISTS (SELECT 1 FROM security.user_role_assignments a WHERE a.role_id = r.id AND a.deleted_at IS NULL)
            """)).Should().Be(1, "el rol propio de la compañía queda fuera del alcance (tenant_id IS NULL)");
    }

    [PostgresFact]
    public async Task Ddl_EsIdempotente_SegundaEjecucionSinErrorNiCambios()
    {
        await using var cn = await Fixture.OpenConnectionAsync();
        await SeedAsync(cn);
        await RunAsync(cn);
        var deletedAt = await ScalarAsync(cn, $"SELECT deleted_at FROM security.roles WHERE {RolGlobal}");
        var rowVersion = await ScalarAsync(cn, $"SELECT row_version FROM security.roles WHERE {RolGlobal}");

        await RunAsync(cn);

        (await ScalarAsync(cn, $"SELECT deleted_at FROM security.roles WHERE {RolGlobal}")).Should().Be(deletedAt);
        (await ScalarAsync(cn, $"SELECT row_version FROM security.roles WHERE {RolGlobal}"))
            .Should().Be(rowVersion, "la segunda corrida no vuelve a escribir el rol");
        (await CountAsync(cn, $"SELECT count(*) FROM security.roles WHERE {RolGlobal}")).Should().Be(1);
    }

    [PostgresFact]
    public async Task Ddl_SinElRol_NoFalla()
    {
        await using var cn = await Fixture.OpenConnectionAsync();

        await RunAsync(cn);

        (await CountAsync(cn, $"SELECT count(*) FROM security.roles WHERE {RolGlobal}")).Should().Be(0);
    }

    [PostgresFact]
    public async Task Down_RestituyeElRol_SusPermisosYLaAsignacionQueCerro()
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

        (await CountAsync(cn, $"SELECT count(*) FROM security.roles WHERE {RolGlobal} AND is_active AND deleted_at IS NULL"))
            .Should().Be(1);
        (await PermisosDelRolGlobalAsync(cn)).Should().Be(2);
        (await AsignacionesVigentesAsync(cn, "backoffice_diagnostico")).Should().Be(1);
    }
}
