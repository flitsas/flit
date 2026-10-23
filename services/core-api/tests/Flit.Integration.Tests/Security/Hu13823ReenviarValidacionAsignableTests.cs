using Flit.Infrastructure.Persistence.Repositories;
using Flit.Infrastructure.Persistence.Sql;
using Flit.Integration.Tests.Postgres;
using Flit.Modules.Security.Application.PermissionOverrides;
using Flit.Modules.Security.Application.PermissionOverrides.GrantModule;
using Flit.Modules.Security.Domain.PermissionOverrides;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace Flit.Integration.Tests.Security;

/// <summary>
/// HU #13823 (Feature #13819, Épica #13817) contra PostgreSQL real: el DDL 150 pone «Reenviar validación de identidad»
/// (<c>AdminTramiteReenviarValidacion</c>) en los roles administradores de Trámites (<c>admin_tramites</c>,
/// <c>ot_admin</c>), así que deja de ser «Solo SA» (HU #13653) y el administrador lo puede asignar. Los otros cinco
/// permisos de gestión avanzada siguen siendo exclusivos de SuperAdmin. El arnés trunca el catálogo entre pruebas,
/// así que cada prueba siembra el módulo y los roles antes de correr el DDL.
/// </summary>
public sealed class Hu13823ReenviarValidacionAsignableTests(PostgresDatabaseFixture fixture) : PostgresTestBase(fixture)
{
    private const string DdlFile = "150-HU13823-reenviar-validacion-asignable.sql";
    private const string MigrationId = "20261022130000_HU13823_ReenviarValidacionAsignable";
    private const string Reenviar = "AdminTramiteReenviarValidacion";
    private static readonly Guid Tenant = new("aaaaaaaa-1382-4300-8000-00000000000a");
    private static readonly Guid AdminCo = new("a1000000-1382-4300-8000-000000000001");
    private static readonly Guid SuperAdmin = new("a4000000-1382-4300-8000-000000000004");

    private static readonly string[] Avanzados =
    [
        "AdminTramiteCambiarEstado", "AdminTramiteAnular", "AdminTramiteLimpiarConsolidado",
        "AdminTramiteCargarConsolidado", Reenviar, "AdminTramiteReasignarGestor",
    ];

    private readonly List<Flit.Infrastructure.Persistence.FlitDbContext> _contexts = [];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public override async ValueTask DisposeAsync()
    {
        foreach (var ctx in _contexts)
        {
            await ctx.DisposeAsync();
        }

        await base.DisposeAsync();
    }

    private static async Task ExecAsync(NpgsqlConnection cn, string sql)
    {
        await using var cmd = new NpgsqlCommand(sql, cn);
        await cmd.ExecuteNonQueryAsync(Ct);
    }

    private static async Task<long> CountAsync(NpgsqlConnection cn, string sql)
    {
        await using var cmd = new NpgsqlCommand(sql, cn);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync(Ct));
    }

    private static async Task<List<string>> SlugsDelRolAsync(NpgsqlConnection cn, string rol)
    {
        await using var cmd = new NpgsqlCommand($"""
            SELECT p.slug FROM security.role_permissions rp
              JOIN security.roles r ON r.id = rp.role_id
              JOIN security.permissions p ON p.id = rp.permission_id
             WHERE r.code = '{rol}' AND r.tenant_id IS NULL
             ORDER BY p.slug
            """, cn);
        await using var reader = await cmd.ExecuteReaderAsync(Ct);
        var slugs = new List<string>();
        while (await reader.ReadAsync(Ct))
        {
            slugs.Add(reader.GetString(0));
        }

        return slugs;
    }

    private ManageUserPermissionOverridesHandler Handler()
    {
        var ctx = NewContext();
        _contexts.Add(ctx);
        var repo = new UserPermissionOverrideRepository(ctx);
        return new ManageUserPermissionOverridesHandler(repo, new PermissionCapCalculator(repo), new GrantModuleOverridesHandler(repo), []);
    }

    /// <summary>Catálogo mínimo: módulo admin-tramites-avanzado (producto trámites) con sus 6 slugs y los dos roles.</summary>
    private async Task<NpgsqlConnection> SeedAsync()
    {
        var cn = await Fixture.OpenConnectionAsync();
        var valores = string.Join(", ", Avanzados.Select(s => $"('{s}')"));
        await ExecAsync(cn, $"""
            INSERT INTO platform.products (code, name, icon) VALUES ('tramites','Tramites','file') ON CONFLICT (code) DO NOTHING;
            INSERT INTO identity.tenants (id, code, legal_name, tax_id, tenant_type, is_active, is_group_parent, created_at)
              VALUES ('{Tenant}','IT-138-A','A','9138230000001','CONCESIONARIO',true,false,now());
            INSERT INTO identity.users (id, email, display_name, status, created_at) VALUES
              ('{AdminCo}','co13823@it.local','Co','active',now()), ('{SuperAdmin}','sa13823@it.local','Sa','active',now());
            INSERT INTO security.modules (code, name, sort_order, is_active, product_code)
              SELECT 'admin-tramites-avanzado','Gestión avanzada',1,true,'tramites'
               WHERE NOT EXISTS (SELECT 1 FROM security.modules WHERE code = 'admin-tramites-avanzado');
            INSERT INTO security.permissions (module_id, slug, name, http_method, route_pattern, is_active, enforced)
              SELECT m.id, v.slug, v.slug, 'POST', '/api/v1/admin/tramites/' || v.slug, true, 'true'
                FROM security.modules m, (VALUES {valores}) AS v(slug)
               WHERE m.code = 'admin-tramites-avanzado'
                 AND NOT EXISTS (SELECT 1 FROM security.permissions p WHERE p.slug = v.slug);
            INSERT INTO security.roles (code, name, is_system, is_active, target_entity_type, product_code, tenant_id)
              SELECT v.code, v.code, true, true, v.target, 'tramites', NULL
                FROM (VALUES ('admin_tramites','COMPANY'), ('ot_admin','TRANSIT_OFFICE')) AS v(code, target)
               WHERE NOT EXISTS (SELECT 1 FROM security.roles r WHERE r.code = v.code AND r.deleted_at IS NULL);
            INSERT INTO security.user_role_assignments (tenant_id, user_id, role_id)
              SELECT '{Tenant}', '{AdminCo}', id FROM security.roles WHERE code = 'admin_tramites';
            """);
        return cn;
    }

    [PostgresFact]
    public async Task Migracion_SeDescubreConAtributosInline_YEstaAplicada()
    {
        await using var ctx = NewContext();
        ctx.Database.GetMigrations().Should().Contain(MigrationId);
        (await ctx.Database.GetAppliedMigrationsAsync(Ct)).Should().Contain(MigrationId);
    }

    [PostgresFact]
    public async Task AC1_LosRolesAdministradoresDeTramitesRecibenSoloElPermisoDeReenviar()
    {
        await using var cn = await SeedAsync();

        await ExecAsync(cn, EmbeddedDdl.LoadUp(DdlFile));

        (await SlugsDelRolAsync(cn, "admin_tramites")).Should().Equal(Reenviar);
        (await SlugsDelRolAsync(cn, "ot_admin")).Should().Equal(Reenviar);
    }

    [PostgresFact]
    public async Task AC1_ElPermisoDejaDeSerSoloSA_YLosDemasAvanzadosSiguenSiendolo()
    {
        await using var cn = await SeedAsync();
        await ExecAsync(cn, EmbeddedDdl.LoadUp(DdlFile));
        var handler = Handler();

        var grantable = await handler.ListGrantableAsync(Tenant, new OverrideActor(SuperAdmin, true), Ct);
        var soloSa = await handler.ListSuperAdminOnlyAsync(Tenant, grantable, Ct);

        var ids = new Dictionary<string, Guid>();
        await using (var cmd = new NpgsqlCommand("SELECT slug, id FROM security.permissions WHERE slug = ANY(@slugs)", cn))
        {
            cmd.Parameters.AddWithValue("slugs", Avanzados);
            await using var reader = await cmd.ExecuteReaderAsync(Ct);
            while (await reader.ReadAsync(Ct))
            {
                ids[reader.GetString(0)] = reader.GetGuid(1);
            }
        }

        soloSa.Should().NotContain(ids[Reenviar]);
        foreach (var otro in Avanzados.Where(s => s != Reenviar))
        {
            soloSa.Should().Contain(ids[otro], $"{otro} sigue siendo exclusivo de SuperAdmin");
        }
    }

    [PostgresFact]
    public async Task AC3_EsIdempotente_SegundaEjecucionNoDuplica()
    {
        await using var cn = await SeedAsync();
        await ExecAsync(cn, EmbeddedDdl.LoadUp(DdlFile));
        var grants = await CountAsync(cn, "SELECT count(*) FROM security.role_permissions");

        await ExecAsync(cn, EmbeddedDdl.LoadUp(DdlFile));

        (await CountAsync(cn, "SELECT count(*) FROM security.role_permissions")).Should().Be(grants);
    }
}
