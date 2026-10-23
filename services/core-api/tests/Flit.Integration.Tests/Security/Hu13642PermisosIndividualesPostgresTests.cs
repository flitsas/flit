using Flit.Infrastructure.Persistence.Repositories;
using Flit.Infrastructure.Persistence.Sql;
using Flit.Integration.Tests.Postgres;
using Flit.Modules.Security.Application.PermissionOverrides.GrantModule;
using Flit.Modules.Security.Domain.PermissionOverrides;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace Flit.Integration.Tests.Security;

/// <summary>
/// HU #13642 (Feature #13101 F-B, Épica #12753, ADR-0073) contra PostgreSQL real: DDL 144 aplicado por la migración
/// <c>20261019140000_HU13642_PermisosIndividuales</c>. AC1 unicidad, AC2 foto del módulo, AC3 RLS (con un rol sin
/// BYPASSRLS, como en <c>RolesPorTenantPostgresTests</c>) y AC4 bitácora append-only.
/// <para>Uso de ejemplo: <c>await SeedAsync(cn); await new UserPermissionOverrideRepository(NewContext()).AddAsync(...)</c></para>
/// </summary>
public sealed class Hu13642PermisosIndividualesPostgresTests(PostgresDatabaseFixture fixture) : PostgresTestBase(fixture)
{
    private const string MigrationId = "20261019140000_HU13642_PermisosIndividuales";
    private static readonly Guid TenantA = new("aaaaaaaa-1364-4000-8000-00000000000a");
    private static readonly Guid TenantB = new("bbbbbbbb-1364-4000-8000-00000000000b");
    private static readonly Guid User = new("cccccccc-1364-4000-8000-00000000000c");
    private static readonly Guid Actor = new("dddddddd-1364-4000-8000-00000000000d");

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

    private static async Task<PostgresException> RejectedAsync(NpgsqlConnection cn, string sql)
    {
        var act = async () => await ExecAsync(cn, sql);
        return (await act.Should().ThrowAsync<PostgresException>()).Which;
    }

    // Módulo 'ict_it' (producto tramites) con 3 acciones activas.
    private async Task<NpgsqlConnection> SeedAsync()
    {
        var cn = await Fixture.OpenConnectionAsync();
        await ExecAsync(cn, $"""
            INSERT INTO platform.products (code, name, icon) VALUES ('tramites','Tramites','file') ON CONFLICT (code) DO NOTHING;
            INSERT INTO identity.tenants (id, code, legal_name, tax_id, tenant_type, is_active, is_group_parent, created_at)
              VALUES ('{TenantA}','IT-UPO-A','A','9136400000001','CONCESIONARIO',true,false,now()),
                     ('{TenantB}','IT-UPO-B','B','9136400000002','CONCESIONARIO',true,false,now());
            INSERT INTO identity.users (id, email, display_name, status, created_at)
              VALUES ('{User}','u13642@it.local','U','active',now()), ('{Actor}','a13642@it.local','A','active',now());
            INSERT INTO security.modules (code, name, sort_order, is_active, product_code) VALUES ('ict_it','ICT IT',1,true,'tramites');
            """);
        foreach (var n in new[] { 1, 2, 3 })
        {
            await AddActionAsync(cn, n);
        }

        return cn;
    }

    private static Task AddActionAsync(NpgsqlConnection cn, int n) => ExecAsync(cn, $"""
        INSERT INTO security.permissions (module_id, slug, name, http_method, route_pattern, is_active)
        SELECT id, 'ict_it.accion{n}', 'Accion {n}', 'GET', '/api/v1/ict-it/{n}', true FROM security.modules WHERE code = 'ict_it';
        """);

    private readonly List<Flit.Infrastructure.Persistence.FlitDbContext> _contexts = [];

    public override async ValueTask DisposeAsync()
    {
        foreach (var ctx in _contexts)
        {
            await ctx.DisposeAsync();
        }

        await base.DisposeAsync();
    }

    private UserPermissionOverrideRepository NewRepository()
    {
        var ctx = NewContext();
        _contexts.Add(ctx);
        return new(ctx);
    }

    private static UserPermissionOverride Override(Guid tenant, Guid permissionId, string effect) =>
        new(Guid.NewGuid(), tenant, User, "tramites", permissionId, effect, PermissionOverrideSources.Individual, null, Actor);

    private static async Task<Guid> PermissionIdAsync(NpgsqlConnection cn, string slug)
    {
        await using var cmd = new NpgsqlCommand($"SELECT id FROM security.permissions WHERE slug = '{slug}'", cn);
        return (Guid)(await cmd.ExecuteScalarAsync(TestContext.Current.CancellationToken))!;
    }

    [PostgresFact]
    public async Task Migracion_SeDescubreConAtributosInline_Y_Ddl_EsIdempotente()
    {
        await using var cn = await Fixture.OpenConnectionAsync();
        await using (var ctx = NewContext())
        {
            (await ctx.Database.GetAppliedMigrationsAsync(TestContext.Current.CancellationToken)).Should().Contain(MigrationId);
        }

        await ExecAsync(cn, EmbeddedDdl.LoadUp("144-HU13642-permisos-individuales.sql"));
        (await CountAsync(cn, "SELECT count(*) FROM pg_policies WHERE tablename IN ('user_permission_overrides','user_permission_audit')"))
            .Should().Be(2);
    }

    [PostgresFact]
    public async Task Down_retira_ambas_tablas_y_la_funcion_del_trigger()
    {
        await using var cn = await Fixture.OpenConnectionAsync();
        var down = typeof(Flit.Infrastructure.Migrations.HU13642_PermisosIndividuales)
            .GetMethod("Down", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        var builder = new Microsoft.EntityFrameworkCore.Migrations.MigrationBuilder("Npgsql.EntityFrameworkCore.PostgreSQL");
        down.Invoke(new Flit.Infrastructure.Migrations.HU13642_PermisosIndividuales(), [builder]);
        var sql = string.Join("\n", builder.Operations.OfType<Microsoft.EntityFrameworkCore.Migrations.Operations.SqlOperation>().Select(o => o.Sql));

        // En una transacción que se revierte: la base efímera es compartida y el reset no restaura el esquema.
        await ExecAsync(cn, "BEGIN");
        try
        {
            await ExecAsync(cn, sql);
            (await CountAsync(cn, "SELECT count(*) FROM pg_tables WHERE tablename IN ('user_permission_overrides','user_permission_audit')")).Should().Be(0);
            (await CountAsync(cn, "SELECT count(*) FROM pg_proc WHERE proname = 'trg_user_permission_audit_immutable'")).Should().Be(0);
        }
        finally
        {
            await ExecAsync(cn, "ROLLBACK");
        }

        (await CountAsync(cn, "SELECT count(*) FROM pg_tables WHERE tablename IN ('user_permission_overrides','user_permission_audit')")).Should().Be(2);
    }

    // AC1
    [PostgresFact]
    public async Task AC1_se_guarda_con_usuario_tenant_producto_permiso_y_efecto_y_un_segundo_registro_se_rechaza()
    {
        await using var cn = await SeedAsync();
        var permiso = await PermissionIdAsync(cn, "ict_it.accion1");
        var repo = NewRepository();

        await repo.AddAsync(TenantA, [Override(TenantA, permiso, PermissionEffects.Deny)], [], TestContext.Current.CancellationToken);

        (await CountAsync(cn, $"""
            SELECT count(*) FROM security.user_permission_overrides
             WHERE tenant_id = '{TenantA}' AND user_id = '{User}' AND product_code = 'tramites'
               AND permission_id = '{permiso}' AND effect = 'deny'
            """)).Should().Be(1);

        var duplicado = async () => await NewRepository().AddAsync(
            TenantA, [Override(TenantA, permiso, PermissionEffects.Allow)], [], TestContext.Current.CancellationToken);
        await duplicado.Should().ThrowAsync<DuplicateUserPermissionOverrideException>();

        // El mismo permiso en otro tenant sí es otro hecho, y una revocación (soft delete) libera el cupo.
        await NewRepository().AddAsync(TenantB, [Override(TenantB, permiso, PermissionEffects.Allow)], [], TestContext.Current.CancellationToken);
        await ExecAsync(cn, $"UPDATE security.user_permission_overrides SET deleted_at = now() WHERE tenant_id = '{TenantA}'");
        await NewRepository().AddAsync(TenantA, [Override(TenantA, permiso, PermissionEffects.Allow)], [], TestContext.Current.CancellationToken);
        (await CountAsync(cn, "SELECT count(*) FROM security.user_permission_overrides")).Should().Be(3);
    }

    // AC2
    [PostgresFact]
    public async Task AC2_modulo_completo_crea_una_fila_por_accion_y_una_accion_agregada_despues_no_queda_otorgada()
    {
        await using var cn = await SeedAsync();
        var handler = new GrantModuleOverridesHandler(NewRepository());

        var result = await handler.HandleAsync(
            new GrantModuleOverridesCommand(TenantA, User, Actor, "ict_it", PermissionEffects.Allow), TestContext.Current.CancellationToken);

        result.Created.Should().Be(3);
        (await CountAsync(cn, $"""
            SELECT count(*) FROM security.user_permission_overrides
             WHERE tenant_id = '{TenantA}' AND source = 'MODULE_SNAPSHOT' AND batch_id = '{result.BatchId}'
            """)).Should().Be(3);
        (await CountAsync(cn, $"SELECT count(*) FROM security.user_permission_audit WHERE batch_id = '{result.BatchId}' AND action = 'MODULE_SNAPSHOT' AND outcome = 'OK'"))
            .Should().Be(3);

        await AddActionAsync(cn, 4);

        (await CountAsync(cn, "SELECT count(*) FROM security.user_permission_overrides")).Should().Be(3);
        (await CountAsync(cn, $"SELECT count(*) FROM security.user_permission_overrides o JOIN security.permissions p ON p.id = o.permission_id WHERE p.slug = 'ict_it.accion4'"))
            .Should().Be(0, "la acción nueva no quedó otorgada");
    }

    // AC3: rol sin BYPASSRLS (la conexión del arnés es owner y bypassa la RLS).
    [PostgresFact]
    public async Task AC3_un_contexto_del_tenant_A_no_ve_los_registros_del_tenant_B()
    {
        await using var cn = await SeedAsync();
        var permiso = await PermissionIdAsync(cn, "ict_it.accion1");
        await NewRepository().AddAsync(TenantA, [Override(TenantA, permiso, "allow")], [], TestContext.Current.CancellationToken);
        await NewRepository().AddAsync(TenantB, [Override(TenantB, permiso, "deny")], [], TestContext.Current.CancellationToken);
        await ExecAsync(cn, $"""
            INSERT INTO security.user_permission_audit (tenant_id, actor_user_id, target_user_id, action) VALUES
              ('{TenantA}','{Actor}','{User}','GRANT'), ('{TenantB}','{Actor}','{User}','GRANT');
            """);

        var probe = "flit_it_rls_" + Guid.NewGuid().ToString("N")[..8];
        try
        {
            await ExecAsync(cn, $"CREATE ROLE {probe}; GRANT USAGE ON SCHEMA security TO {probe}; GRANT SELECT ON security.user_permission_overrides, security.user_permission_audit TO {probe};");
            await ExecAsync(cn, $"SET ROLE {probe}");
            await ExecAsync(cn, $"SELECT set_config('app.current_tenant_id', '{TenantA}', false)");

            (await CountAsync(cn, "SELECT count(*) FROM security.user_permission_overrides")).Should().Be(1);
            (await CountAsync(cn, $"SELECT count(*) FROM security.user_permission_overrides WHERE tenant_id = '{TenantB}'")).Should().Be(0);
            (await CountAsync(cn, "SELECT count(*) FROM security.user_permission_audit")).Should().Be(1);

            await ExecAsync(cn, "RESET app.current_tenant_id");
            (await CountAsync(cn, "SELECT count(*) FROM security.user_permission_overrides")).Should().Be(0, "sin contexto de tenant no ve nada");
        }
        finally
        {
            await ExecAsync(cn, "RESET ROLE; RESET app.current_tenant_id");
            await ExecAsync(cn, $"DROP OWNED BY {probe}; DROP ROLE IF EXISTS {probe};");
        }
    }

    // AC4
    [PostgresFact]
    public async Task AC4_la_bitacora_es_append_only_UPDATE_y_DELETE_los_rechaza_la_base()
    {
        await using var cn = await SeedAsync();
        await ExecAsync(cn, $"""
            INSERT INTO security.user_permission_audit (tenant_id, actor_user_id, target_user_id, action, outcome, reason_code)
            VALUES ('{TenantA}','{Actor}','{User}','GRANT','REJECTED','OUT_OF_CAP');
            """);

        var update = await RejectedAsync(cn, "UPDATE security.user_permission_audit SET reason_code = 'X'");
        update.SqlState.Should().Be(PostgresErrorCodes.CheckViolation);
        update.MessageText.Should().Contain("append-only");
        var delete = await RejectedAsync(cn, "DELETE FROM security.user_permission_audit");
        delete.SqlState.Should().Be(PostgresErrorCodes.CheckViolation);

        (await CountAsync(cn, "SELECT count(*) FROM security.user_permission_audit WHERE reason_code = 'OUT_OF_CAP'")).Should().Be(1);
    }
}
