using Flit.Infrastructure.Migrations;
using Flit.Infrastructure.Persistence.Sql;
using Flit.Integration.Tests.Postgres;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Npgsql;
using Xunit;

namespace Flit.Integration.Tests.Platform;

/// <summary>
/// HU #14694 (ADR-0093, plan docs/impuestos/plan-implementacion.md §4.3) contra PostgreSQL real: el DDL 205 siembra el
/// producto <c>impuestos</c>, el rol de sistema <c>admin_impuestos</c> para los AdminCompany activos, el ascenso de
/// Gestor a Admin (solo en Impuestos, gate LT G-3) y la tabla <c>platform.tenant_product_provisioning</c>.
/// <para>
/// Uso de ejemplo: sembrar AdminCompany y un usuario con <c>gestor_impuestos</c>, aplicar el DDL y asignarle AdminCompany
/// → su <c>gestor_impuestos</c> queda cerrado y tiene <c>admin_impuestos</c> activo.
/// </para>
/// <para>
/// <c>security.roles</c> y las asignaciones se truncan entre pruebas (<see cref="PostgresDatabaseFixture"/>), así que
/// cada prueba siembra AdminCompany y vuelve a aplicar el DDL (idempotente); <c>platform.products</c> se preserva.
/// </para>
/// </summary>
public sealed class Ddl205ImpuestosTests(PostgresDatabaseFixture fixture) : PostgresTestBase(fixture)
{
    private const string DdlFile = "205-impuestos-producto-y-aprovisionamiento.sql";
    private const string MigrationSuffix = "_ImpuestosProductoYAprovisionamiento";
    private const string Tenant = "14694000-0000-4000-8000-000000000001";
    private const string UserAdmin = "14694000-0000-4000-8000-0000000000a1";
    private const string UserGestor = "14694000-0000-4000-8000-0000000000a2";
    private const string UserAmbos = "14694000-0000-4000-8000-0000000000a3";

    /// <summary>Actor de sistema con que el DDL 205 (3a) cierra los gestor_impuestos que asciende; el Down reabre solo esos.</summary>
    private const string ActorAscenso = "00000000-0000-4000-8000-000000000205";

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

    private static Task RunDdlAsync(NpgsqlConnection cn) => ExecAsync(cn, EmbeddedDdl.LoadUp(DdlFile));

    // Empresa, tres usuarios, el rol AdminCompany y los roles de producto que crearían los manifiestos (gestor_impuestos)
    // o el DDL 126 (admin_diagnostico), más un rol no admin de Diagnóstico para comprobar que esos productos no cambian.
    private static Task SeedAsync(NpgsqlConnection cn) => ExecAsync(cn, $"""
        INSERT INTO identity.tenants (id, code, legal_name, tax_id, tenant_type, is_active, is_group_parent, created_at) VALUES
          ('{Tenant}','IT-14694-A','Compañía 14694','9146940000001','RENTING',true,false,now());
        INSERT INTO identity.users (id, email, display_name, status, home_tenant_id, created_at) VALUES
          ('{UserAdmin}','ad14694@it.local','Ad','active','{Tenant}',now()),
          ('{UserGestor}','ge14694@it.local','Ge','active','{Tenant}',now()),
          ('{UserAmbos}','am14694@it.local','Am','active','{Tenant}',now());
        INSERT INTO security.roles (code, name, is_system, is_active, target_entity_type, product_code, tenant_id) VALUES
          ('AdminCompany','Administrador de Compañía',true,true,'COMPANY','plataforma',NULL),
          ('gestor_impuestos','Gestor Impuestos',true,true,'COMPANY','impuestos',NULL),
          ('admin_diagnostico','Administrador de Diagnóstico',true,true,'COMPANY','diagnostico',NULL),
          ('usuario_diagnostico','Usuario de Diagnóstico',true,true,'COMPANY','diagnostico',NULL);
        """);

    private static Task AsignarAsync(NpgsqlConnection cn, string userId, string roleCode) => ExecAsync(cn, $"""
        INSERT INTO security.user_role_assignments (tenant_id, user_id, role_id)
          SELECT '{Tenant}', '{userId}', id FROM security.roles WHERE code = '{roleCode}' AND tenant_id IS NULL AND deleted_at IS NULL;
        """);

    private static Task<long> VigentesAsync(NpgsqlConnection cn, string userId, string roleCode) => CountAsync(cn, $"""
        SELECT count(*) FROM security.user_role_assignments a JOIN security.roles r ON r.id = a.role_id
         WHERE a.user_id = '{userId}' AND a.tenant_id = '{Tenant}' AND r.code = '{roleCode}' AND a.deleted_at IS NULL
        """);

    private static Task<long> CerradasAsync(NpgsqlConnection cn, string userId, string roleCode) => CountAsync(cn, $"""
        SELECT count(*) FROM security.user_role_assignments a JOIN security.roles r ON r.id = a.role_id
         WHERE a.user_id = '{userId}' AND a.tenant_id = '{Tenant}' AND r.code = '{roleCode}' AND a.deleted_at IS NOT NULL
        """);

    // ── Migración ─────────────────────────────────────────────────────────────────

    [PostgresFact]
    public async Task Migracion_SeDescubreYEstaAplicada()
    {
        await using var ctx = NewContext();
        ctx.Database.GetMigrations().Should().Contain(m => m.EndsWith(MigrationSuffix, StringComparison.Ordinal));
        (await ctx.Database.GetAppliedMigrationsAsync(TestContext.Current.CancellationToken))
            .Should().Contain(m => m.EndsWith(MigrationSuffix, StringComparison.Ordinal));
    }

    // ── 1. Producto en el catálogo (F1a-CF1) ──────────────────────────────────────

    [PostgresFact]
    public async Task Catalogo_TraeImpuestosInactivo_ConSuIconoYOrden()
    {
        // BLOQ-1 (a): nace inactivo; se activa por ambiente (docs/impuestos/guia-despliegue.md).
        await using var cn = await Fixture.OpenConnectionAsync();
        (await CountAsync(cn, """
            SELECT count(*) FROM platform.products
             WHERE code = 'impuestos' AND name = 'Impuestos' AND icon = 'receipt' AND status = 'inactive' AND sort_order = 60
            """)).Should().Be(1);
    }

    // ── 2. Rol admin_impuestos y siembra a los AdminCompany activos (F5b-CF2, D-ROL-ADMIN) ─────────

    [PostgresFact]
    public async Task Ddl_CreaElRolDeSistema_YLoSiembraALosAdminCompanyActivos()
    {
        await using var cn = await Fixture.OpenConnectionAsync();
        await SeedAsync(cn);
        // AdminCompany asignado antes de que exista admin_impuestos: el espejo del DDL 126 todavía no tiene a quién dar.
        await AsignarAsync(cn, UserAdmin, "AdminCompany");
        (await VigentesAsync(cn, UserAdmin, "admin_impuestos")).Should().Be(0);

        await RunDdlAsync(cn);

        (await CountAsync(cn, """
            SELECT count(*) FROM security.roles
             WHERE code = 'admin_impuestos' AND tenant_id IS NULL AND is_system AND is_active AND deleted_at IS NULL
               AND target_entity_type = 'COMPANY' AND product_code = 'impuestos'
            """)).Should().Be(1);
        (await VigentesAsync(cn, UserAdmin, "admin_impuestos")).Should().Be(1, "la siembra le da admin_impuestos al AdminCompany activo");
        (await VigentesAsync(cn, UserGestor, "admin_impuestos")).Should().Be(0, "quien no es AdminCompany no recibe el rol");
    }

    // ── 3. Ascenso de Gestor a Admin, solo en Impuestos (F5b-CF2, A-H16, gate LT G-3) ──────────────

    [PostgresFact]
    public async Task Trigger_AlAsignarAdminCompany_CierraElGestorImpuestos_YDaAdminImpuestos()
    {
        await using var cn = await Fixture.OpenConnectionAsync();
        await SeedAsync(cn);
        await RunDdlAsync(cn);
        await AsignarAsync(cn, UserGestor, "gestor_impuestos");

        await AsignarAsync(cn, UserGestor, "AdminCompany");

        (await VigentesAsync(cn, UserGestor, "gestor_impuestos")).Should().Be(0);
        (await CerradasAsync(cn, UserGestor, "gestor_impuestos")).Should().Be(1, "el gestor se cierra (soft delete), no se borra");
        (await VigentesAsync(cn, UserGestor, "admin_impuestos")).Should().Be(1);
    }

    [PostgresFact]
    public async Task Trigger_EnOtrosProductos_ConservaElRolQueYaTenia_SinAscenso()
    {
        await using var cn = await Fixture.OpenConnectionAsync();
        await SeedAsync(cn);
        await RunDdlAsync(cn);
        await AsignarAsync(cn, UserGestor, "usuario_diagnostico");

        await AsignarAsync(cn, UserGestor, "AdminCompany");

        (await VigentesAsync(cn, UserGestor, "usuario_diagnostico")).Should().Be(1, "fuera de Impuestos el espejo no reemplaza el rol que ya hay");
        (await VigentesAsync(cn, UserGestor, "admin_diagnostico")).Should().Be(0);
        (await VigentesAsync(cn, UserGestor, "admin_impuestos")).Should().Be(1, "sin rol previo en Impuestos recibe admin_impuestos");
    }

    [PostgresFact]
    public async Task Ddl_AscendeAlAdminCompanyQueYaEraGestorImpuestos()
    {
        await using var cn = await Fixture.OpenConnectionAsync();
        await SeedAsync(cn);
        await AsignarAsync(cn, UserAmbos, "gestor_impuestos");
        await AsignarAsync(cn, UserAmbos, "AdminCompany");
        (await VigentesAsync(cn, UserAmbos, "gestor_impuestos")).Should().Be(1, "antes del DDL no hay ascenso");

        await RunDdlAsync(cn);

        (await VigentesAsync(cn, UserAmbos, "gestor_impuestos")).Should().Be(0);
        (await VigentesAsync(cn, UserAmbos, "admin_impuestos")).Should().Be(1);
    }

    [PostgresFact]
    public async Task Trigger_QuitarAdminCompany_CierraAdminImpuestos()
    {
        await using var cn = await Fixture.OpenConnectionAsync();
        await SeedAsync(cn);
        await RunDdlAsync(cn);
        await AsignarAsync(cn, UserAdmin, "AdminCompany");
        (await VigentesAsync(cn, UserAdmin, "admin_impuestos")).Should().Be(1);

        await ExecAsync(cn, $"""
            UPDATE security.user_role_assignments a SET deleted_at = now()
              FROM security.roles r
             WHERE r.id = a.role_id AND r.code = 'AdminCompany' AND a.user_id = '{UserAdmin}' AND a.deleted_at IS NULL;
            """);

        (await VigentesAsync(cn, UserAdmin, "admin_impuestos")).Should().Be(0, "el espejo del DDL 126 sigue cerrando el admin del producto");
    }

    [PostgresFact]
    public async Task Ddl_EsIdempotente()
    {
        await using var cn = await Fixture.OpenConnectionAsync();
        await SeedAsync(cn);
        await AsignarAsync(cn, UserAdmin, "AdminCompany");

        await RunDdlAsync(cn);
        await RunDdlAsync(cn);

        (await CountAsync(cn, "SELECT count(*) FROM platform.products WHERE code = 'impuestos'")).Should().Be(1);
        (await CountAsync(cn, "SELECT count(*) FROM security.roles WHERE code = 'admin_impuestos' AND tenant_id IS NULL")).Should().Be(1);
        (await VigentesAsync(cn, UserAdmin, "admin_impuestos")).Should().Be(1);
    }

    // ── 3a. Ascenso del DDL: actor centinela y AdminCompany vigente (DB Media/Baja) ────────────────────────

    [PostgresFact]
    public async Task Ddl_CierraElGestorAscendido_ConElActorCentinelaDeSistema()
    {
        await using var cn = await Fixture.OpenConnectionAsync();
        await SeedAsync(cn);
        await AsignarAsync(cn, UserAmbos, "gestor_impuestos");
        await AsignarAsync(cn, UserAmbos, "AdminCompany");

        await RunDdlAsync(cn);

        (await CountAsync(cn, $"""
            SELECT count(*) FROM security.user_role_assignments a JOIN security.roles r ON r.id = a.role_id
             WHERE a.user_id = '{UserAmbos}' AND r.code = 'gestor_impuestos' AND a.deleted_at IS NOT NULL
               AND a.deleted_by = '{ActorAscenso}'
            """)).Should().Be(1, "el Down distingue por este actor los cierres del ascenso");
    }

    [PostgresFact]
    public async Task Ddl_NoAscendeSiElRolAdminCompanyEstaBorrado()
    {
        await using var cn = await Fixture.OpenConnectionAsync();
        await SeedAsync(cn);
        await AsignarAsync(cn, UserAmbos, "gestor_impuestos");
        await AsignarAsync(cn, UserAmbos, "AdminCompany");
        await ExecAsync(cn, "UPDATE security.roles SET deleted_at = now() WHERE code = 'AdminCompany' AND tenant_id IS NULL");

        await RunDdlAsync(cn);

        (await VigentesAsync(cn, UserAmbos, "gestor_impuestos")).Should().Be(1, "igual que el trigger, un AdminCompany borrado no cuenta");
    }

    // ── Down de la migración ─────────────────────────────────────────────────────────────

    private static string DownSql() =>
        string.Join(Environment.NewLine, new ImpuestosProductoYAprovisionamiento().DownOperations.OfType<SqlOperation>().Select(o => o.Sql));

    /// <summary>Corre el Down y deja el esquema como estaba (Up otra vez) para las demás pruebas de la colección.</summary>
    private static async Task ConDownAsync(NpgsqlConnection cn, Func<Task> verificar)
    {
        try
        {
            await ExecAsync(cn, DownSql());
            await verificar();
        }
        finally
        {
            await RunDdlAsync(cn);
        }
    }

    [PostgresFact]
    public async Task Down_ReabreSoloLosGestoresQueCerroElAscensoDelDdl()
    {
        await using var cn = await Fixture.OpenConnectionAsync();
        await SeedAsync(cn);
        await AsignarAsync(cn, UserAmbos, "gestor_impuestos");
        await AsignarAsync(cn, UserAmbos, "AdminCompany");
        await RunDdlAsync(cn);
        // Otro gestor cerrado por una persona (no por el DDL): el Down no lo toca.
        await AsignarAsync(cn, UserGestor, "gestor_impuestos");
        await ExecAsync(cn, $"""
            UPDATE security.user_role_assignments a SET deleted_at = now(), deleted_by = '{UserAdmin}'
              FROM security.roles r
             WHERE r.id = a.role_id AND r.code = 'gestor_impuestos' AND a.user_id = '{UserGestor}';
            """);

        await ConDownAsync(cn, async () =>
        {
            (await VigentesAsync(cn, UserAmbos, "gestor_impuestos")).Should().Be(1, "lo cerró el ascenso del DDL");
            (await CountAsync(cn, $"""
                SELECT count(*) FROM security.user_role_assignments a JOIN security.roles r ON r.id = a.role_id
                 WHERE a.user_id = '{UserAmbos}' AND r.code = 'gestor_impuestos' AND a.deleted_by IS NOT NULL
                """)).Should().Be(0);
            (await VigentesAsync(cn, UserGestor, "gestor_impuestos")).Should().Be(0, "lo cerró una persona, no el DDL");
        });
    }

    [PostgresFact]
    public async Task Down_BorraLasInvitacionesConAdminImpuestos_AntesDeBorrarElRol()
    {
        await using var cn = await Fixture.OpenConnectionAsync();
        await SeedAsync(cn);
        await RunDdlAsync(cn);
        await ExecAsync(cn, $"""
            INSERT INTO security.user_invitations (tenant_id, email, role_id, token_hash, invited_by)
            SELECT '{Tenant}', 'inv14694@it.local', id, 'hash-14694-down', '{UserAdmin}'
              FROM security.roles WHERE code = 'admin_impuestos' AND tenant_id IS NULL;
            """);

        await ConDownAsync(cn, async () =>
        {
            (await CountAsync(cn, "SELECT count(*) FROM security.roles WHERE code = 'admin_impuestos' AND tenant_id IS NULL"))
                .Should().Be(0);
            (await CountAsync(cn, "SELECT count(*) FROM security.user_invitations WHERE token_hash = 'hash-14694-down'"))
                .Should().Be(0);
        });
    }

    // ── 4. platform.tenant_product_provisioning (F1b-CF1, decisión R1) ─────────────────────────────

    [PostgresFact]
    public async Task Provisioning_UnaFilaPorEmpresaYProducto_ConFkAlCatalogo()
    {
        await using var cn = await Fixture.OpenConnectionAsync();
        await SeedAsync(cn);
        const string Insert = """
            INSERT INTO platform.tenant_product_provisioning (tenant_id, product_code, status, reported_version, reported_at)
            VALUES ('{0}', '{1}', 'PENDIENTE', 1, now())
            """;

        await ExecAsync(cn, string.Format(Insert, Tenant, "impuestos"));

        var duplicada = () => ExecAsync(cn, string.Format(Insert, Tenant, "impuestos"));
        (await duplicada.Should().ThrowAsync<PostgresException>())
            .Which.ConstraintName.Should().Be("uq_tenant_product_provisioning_tenant_product");

        var sinProducto = () => ExecAsync(cn, string.Format(Insert, Tenant, "flotas"));
        (await sinProducto.Should().ThrowAsync<PostgresException>())
            .Which.ConstraintName.Should().Be("fk_tenant_product_provisioning_products");

        var plataforma = () => ExecAsync(cn, string.Format(Insert, Tenant, "plataforma"));
        (await plataforma.Should().ThrowAsync<PostgresException>())
            .Which.ConstraintName.Should().Be("ck_tenant_product_provisioning_not_plataforma");

        var versionCero = () => ExecAsync(cn, $"""
            UPDATE platform.tenant_product_provisioning SET reported_version = 0 WHERE tenant_id = '{Tenant}'
            """);
        (await versionCero.Should().ThrowAsync<PostgresException>())
            .Which.ConstraintName.Should().Be("ck_tenant_product_provisioning_reported_version");
    }

    [PostgresFact]
    public async Task Provisioning_Actualizar_SubeRowVersion_YDejaRastroDeAuditoria()
    {
        await using var cn = await Fixture.OpenConnectionAsync();
        await SeedAsync(cn);
        await ExecAsync(cn, $"""
            INSERT INTO platform.tenant_product_provisioning (tenant_id, product_code, status, reported_version, reported_at)
            VALUES ('{Tenant}', 'impuestos', 'PENDIENTE', 1, now());
            UPDATE platform.tenant_product_provisioning SET status = 'LISTO', signature_confirmed = false, reported_version = 2
             WHERE tenant_id = '{Tenant}' AND product_code = 'impuestos';
            """);

        (await CountAsync(cn, $"""
            SELECT row_version FROM platform.tenant_product_provisioning WHERE tenant_id = '{Tenant}'
            """)).Should().Be(1);
        (await CountAsync(cn, """
            SELECT count(*) FROM audit.audit_logs
             WHERE schema_name = 'platform' AND table_name = 'tenant_product_provisioning' AND action IN ('I', 'U')
            """)).Should().Be(2);
    }

    [PostgresFact]
    public async Task Provisioning_SeBorraConLaEmpresa()
    {
        await using var cn = await Fixture.OpenConnectionAsync();
        await SeedAsync(cn);
        await ExecAsync(cn, $"""
            INSERT INTO platform.tenant_product_provisioning (tenant_id, product_code, status, reported_version, reported_at)
            VALUES ('{Tenant}', 'impuestos', 'PENDIENTE', 1, now());
            """);

        (await CountAsync(cn, $"""
            SELECT count(*) FROM information_schema.referential_constraints
             WHERE constraint_name = 'fk_tenant_product_provisioning_tenants' AND delete_rule = 'CASCADE'
            """)).Should().Be(1);
    }
}
