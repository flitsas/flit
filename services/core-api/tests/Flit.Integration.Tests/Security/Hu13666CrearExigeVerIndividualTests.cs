using Flit.Infrastructure.Persistence.Repositories;
using Flit.Integration.Tests.Postgres;
using Flit.Modules.Security.Application.PermissionOverrides;
using Flit.Modules.Security.Application.PermissionOverrides.GrantModule;
using Flit.Modules.Security.Domain.PermissionOverrides;
using FluentAssertions;
using Npgsql;
using Xunit;

namespace Flit.Integration.Tests.Security;

/// <summary>
/// HU #13666 (Feature #13636 F-F, Épica #12753) contra PostgreSQL real: la regla «crear exige ver» aplicada a los
/// permisos INDIVIDUALES de una familia de trámite (<c>matriculas.read</c> / <c>matriculas.create</c>).
/// </summary>
public sealed class Hu13666CrearExigeVerIndividualTests(PostgresDatabaseFixture fixture) : PostgresTestBase(fixture)
{
    private static readonly Guid Tenant = new("aaaaaaaa-1366-4400-8000-00000000000a");
    private static readonly Guid Target = new("a3000000-1366-4400-8000-000000000003");
    private static readonly Guid SuperAdmin = new("a4000000-1366-4400-8000-000000000004");

    private readonly List<Flit.Infrastructure.Persistence.FlitDbContext> _contexts = [];
    private Guid _read;
    private Guid _create;

    public override async ValueTask DisposeAsync()
    {
        foreach (var ctx in _contexts)
        {
            await ctx.DisposeAsync();
        }

        await base.DisposeAsync();
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static OverrideActor Sa => new(SuperAdmin, true);

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

    private static async Task<Guid> IdAsync(NpgsqlConnection cn, string sql)
    {
        await using var cmd = new NpgsqlCommand(sql, cn);
        return (Guid)(await cmd.ExecuteScalarAsync(Ct))!;
    }

    private ManageUserPermissionOverridesHandler Handler()
    {
        var ctx = NewContext();
        _contexts.Add(ctx);
        var repo = new UserPermissionOverrideRepository(ctx);
        return new ManageUserPermissionOverridesHandler(repo, new PermissionCapCalculator(repo), new GrantModuleOverridesHandler(repo), []);
    }

    private static Task GiveRoleAsync(NpgsqlConnection cn, string slug) => ExecAsync(cn, $"""
        INSERT INTO security.role_permissions (role_id, permission_id)
          SELECT r.id, p.id FROM security.roles r, security.permissions p WHERE r.code = 'it66_rol' AND p.slug = '{slug}';
        """);

    /// <summary>Target con un rol que da exactamente los slugs indicados (ninguno si va vacío).</summary>
    private async Task<NpgsqlConnection> SeedAsync(params string[] roleSlugs)
    {
        var cn = await Fixture.OpenConnectionAsync();
        // El arnés trunca el catálogo entre pruebas: se siembra el módulo y el par de Matrículas como los deja el DDL 146 (enforced = true).
        await ExecAsync(cn, $"""
            INSERT INTO platform.products (code, name, icon) VALUES ('tramites','Tramites','file') ON CONFLICT (code) DO NOTHING;
            INSERT INTO security.modules (code, name, sort_order, is_active, product_code) VALUES ('tramites-matriculas','Trámites: Matrículas',1,true,'tramites');
            INSERT INTO security.permissions (module_id, slug, name, http_method, route_pattern, is_active, enforced)
              SELECT id, v.slug, v.slug, v.m, '/api/v1/tramites?family=MATRICULAS', true, 'true'
                FROM security.modules, (VALUES ('matriculas.read','GET'), ('matriculas.create','POST')) AS v(slug, m)
               WHERE code = 'tramites-matriculas';
            INSERT INTO identity.tenants (id, code, legal_name, tax_id, tenant_type, is_active, is_group_parent, created_at)
              VALUES ('{Tenant}','IT-66-A','A','9136660000001','CONCESIONARIO',true,false,now());
            INSERT INTO identity.users (id, email, display_name, status, created_at) VALUES
              ('{Target}','t13666@it.local','T','active',now()), ('{SuperAdmin}','sa13666@it.local','Sa','active',now());
            INSERT INTO security.roles (code, name, is_system, is_active, target_entity_type, product_code)
              VALUES ('it66_rol','Rol 66',false,true,'COMPANY','tramites');
            INSERT INTO security.user_role_assignments (tenant_id, user_id, role_id)
              SELECT '{Tenant}', '{Target}', id FROM security.roles WHERE code = 'it66_rol';
            """);
        foreach (var slug in roleSlugs)
        {
            await GiveRoleAsync(cn, slug);
        }

        _read = await IdAsync(cn, "SELECT id FROM security.permissions WHERE slug = 'matriculas.read'");
        _create = await IdAsync(cn, "SELECT id FROM security.permissions WHERE slug = 'matriculas.create'");
        return cn;
    }

    private static Task<long> ActiveAsync(NpgsqlConnection cn, Guid permission, string effect) => CountAsync(cn, $"""
        SELECT count(*) FROM security.user_permission_overrides
         WHERE tenant_id = '{Tenant}' AND user_id = '{Target}' AND permission_id = '{permission}' AND effect = '{effect}' AND deleted_at IS NULL
        """);

    private static Task<long> RejectedAsync(NpgsqlConnection cn) => CountAsync(cn, $"""
        SELECT count(*) FROM security.user_permission_audit
         WHERE tenant_id = '{Tenant}' AND target_user_id = '{Target}' AND permission_slug = 'matriculas.create'
           AND outcome = 'REJECTED' AND reason_code = 'FAMILY_CREATE_REQUIRES_READ'
        """);

    [PostgresFact]
    public async Task Otorgar_crear_sin_ver_efectivo_rechaza_y_queda_auditado_como_REJECTED()
    {
        await using var cn = await SeedAsync();

        var act = async () => await Handler().SetAsync(Tenant, Sa, Target, _create, "allow", Ct);

        (await act.Should().ThrowAsync<PermissionOverrideRejectedException>()).Which.ReasonCode.Should().Be("FAMILY_CREATE_REQUIRES_READ");
        (await ActiveAsync(cn, _create, "allow")).Should().Be(0);
        (await RejectedAsync(cn)).Should().Be(1);
    }

    [PostgresFact]
    public async Task Otorgar_crear_con_ver_individual_se_acepta()
    {
        await using var cn = await SeedAsync();
        await Handler().SetAsync(Tenant, Sa, Target, _read, "allow", Ct);

        await Handler().SetAsync(Tenant, Sa, Target, _create, "allow", Ct);

        (await ActiveAsync(cn, _create, "allow")).Should().Be(1);
    }

    [PostgresFact]
    public async Task Otorgar_crear_con_ver_heredado_del_rol_se_acepta()
    {
        await using var cn = await SeedAsync("matriculas.read");

        await Handler().SetAsync(Tenant, Sa, Target, _create, "allow", Ct);

        (await ActiveAsync(cn, _create, "allow")).Should().Be(1);
    }

    [PostgresFact]
    public async Task Ver_denegado_individualmente_no_cuenta_aunque_el_rol_lo_de()
    {
        await using var cn = await SeedAsync("matriculas.read");
        await Handler().SetAsync(Tenant, Sa, Target, _read, "deny", Ct);

        var act = async () => await Handler().SetAsync(Tenant, Sa, Target, _create, "allow", Ct);

        (await act.Should().ThrowAsync<PermissionOverrideRejectedException>()).Which.ReasonCode.Should().Be("FAMILY_CREATE_REQUIRES_READ");
        (await ActiveAsync(cn, _create, "allow")).Should().Be(0);
    }

    [PostgresFact]
    public async Task Denegar_ver_retira_el_allow_individual_de_crear()
    {
        await using var cn = await SeedAsync();
        await Handler().SetAsync(Tenant, Sa, Target, _read, "allow", Ct);
        await Handler().SetAsync(Tenant, Sa, Target, _create, "allow", Ct);

        await Handler().SetAsync(Tenant, Sa, Target, _read, "deny", Ct);

        (await ActiveAsync(cn, _read, "deny")).Should().Be(1);
        (await ActiveAsync(cn, _create, "allow")).Should().Be(0);
        (await ActiveAsync(cn, _create, "deny")).Should().Be(0);
        (await CountAsync(cn, $"SELECT count(*) FROM security.user_permission_audit WHERE target_user_id = '{Target}' AND permission_slug = 'matriculas.create' AND action = 'REVOKE' AND outcome = 'OK'")).Should().Be(1);
    }

    [PostgresFact]
    public async Task Denegar_ver_cuando_el_rol_da_crear_deja_crear_denegado()
    {
        await using var cn = await SeedAsync("matriculas.read", "matriculas.create");

        await Handler().SetAsync(Tenant, Sa, Target, _read, "deny", Ct);

        (await ActiveAsync(cn, _read, "deny")).Should().Be(1);
        (await ActiveAsync(cn, _create, "deny")).Should().Be(1);
    }

    [PostgresFact]
    public async Task Quitar_el_allow_de_ver_cuando_el_rol_no_lo_da_arrastra_el_allow_de_crear()
    {
        await using var cn = await SeedAsync();
        await Handler().SetAsync(Tenant, Sa, Target, _read, "allow", Ct);
        await Handler().SetAsync(Tenant, Sa, Target, _create, "allow", Ct);

        (await Handler().RemoveAsync(Tenant, Sa, Target, _read, Ct)).Should().BeTrue();

        (await ActiveAsync(cn, _read, "allow")).Should().Be(0);
        (await ActiveAsync(cn, _create, "allow")).Should().Be(0);
    }

    [PostgresFact]
    public async Task Quitar_el_allow_de_ver_cuando_el_rol_lo_da_conserva_crear()
    {
        await using var cn = await SeedAsync("matriculas.read");
        await Handler().SetAsync(Tenant, Sa, Target, _read, "allow", Ct);
        await Handler().SetAsync(Tenant, Sa, Target, _create, "allow", Ct);

        await Handler().RemoveAsync(Tenant, Sa, Target, _read, Ct);

        (await ActiveAsync(cn, _create, "allow")).Should().Be(1);
    }
}
