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
/// HU #13644 (Feature #13101 F-B, Épica #12753) contra PostgreSQL real: otorgar/denegar/quitar permisos individuales
/// dentro del tope del administrador (roles reales en <c>role_permissions</c>) y su bitácora. AC1 a AC6.
/// </summary>
public sealed class Hu13644PermisosIndividualesTopeTests(PostgresDatabaseFixture fixture) : PostgresTestBase(fixture)
{
    private static readonly Guid Tenant = new("aaaaaaaa-1364-4400-8000-00000000000a");
    private static readonly Guid AdminCo = new("a1000000-1364-4400-8000-000000000001");
    private static readonly Guid AdminOt = new("a2000000-1364-4400-8000-000000000002");
    private static readonly Guid Target = new("a3000000-1364-4400-8000-000000000003");
    private static readonly Guid SuperAdmin = new("a4000000-1364-4400-8000-000000000004");
    private static readonly Guid Other = new("a5000000-1364-4400-8000-000000000005");

    private readonly List<Flit.Infrastructure.Persistence.FlitDbContext> _contexts = [];
    private Guid _rep;
    private Guid _pii;
    private Guid _off;

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
        await cmd.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    private static async Task<long> CountAsync(NpgsqlConnection cn, string sql)
    {
        await using var cmd = new NpgsqlCommand(sql, cn);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync(TestContext.Current.CancellationToken));
    }

    private static async Task<Guid> IdAsync(NpgsqlConnection cn, string sql)
    {
        await using var cmd = new NpgsqlCommand(sql, cn);
        return (Guid)(await cmd.ExecuteScalarAsync(TestContext.Current.CancellationToken))!;
    }

    private ManageUserPermissionOverridesHandler Handler()
    {
        var ctx = NewContext();
        _contexts.Add(ctx);
        var repo = new UserPermissionOverrideRepository(ctx);
        return new ManageUserPermissionOverridesHandler(repo, new PermissionCapCalculator(repo), new GrantModuleOverridesHandler(repo), []);
    }

    // AdminCo: rol con reportes.read. AdminOt: rol vacío + un allow individual de reportes.read. Target: sin nada.
    private async Task<NpgsqlConnection> SeedAsync()
    {
        var cn = await Fixture.OpenConnectionAsync();
        await ExecAsync(cn, $"""
            INSERT INTO platform.products (code, name, icon) VALUES ('tramites','Tramites','file') ON CONFLICT (code) DO NOTHING;
            INSERT INTO identity.tenants (id, code, legal_name, tax_id, tenant_type, is_active, is_group_parent, created_at)
              VALUES ('{Tenant}','IT-44-A','A','9136440000001','CONCESIONARIO',true,false,now());
            INSERT INTO identity.users (id, email, display_name, status, created_at) VALUES
              ('{AdminCo}','co13644@it.local','Co','active',now()), ('{AdminOt}','ot13644@it.local','Ot','active',now()),
              ('{Target}','t13644@it.local','T','active',now()), ('{SuperAdmin}','sa13644@it.local','Sa','active',now()),
              ('{Other}','o13644@it.local','O','active',now());
            INSERT INTO security.modules (code, name, sort_order, is_active, product_code) VALUES ('it44_m','IT 44',1,true,'tramites');
            INSERT INTO security.permissions (module_id, slug, name, http_method, route_pattern, is_active, enforced)
              SELECT id, v.slug, v.slug, 'GET', '/api/v1/it44/' || v.slug, true, v.enforced
                FROM security.modules, (VALUES ('it44.reportes.read','true'), ('it44.pii.reveal','true'), ('it44.off','false')) AS v(slug, enforced)
               WHERE code = 'it44_m';
            INSERT INTO security.roles (code, name, is_system, is_active, target_entity_type, product_code) VALUES
              ('it44_admin_co','Admin co',false,true,'COMPANY','tramites'), ('it44_admin_ot','Admin ot',false,true,'COMPANY','tramites');
            INSERT INTO security.role_permissions (role_id, permission_id)
              SELECT r.id, p.id FROM security.roles r, security.permissions p WHERE r.code = 'it44_admin_co' AND p.slug = 'it44.reportes.read';
            INSERT INTO security.user_role_assignments (tenant_id, user_id, role_id)
              SELECT '{Tenant}', '{AdminCo}', id FROM security.roles WHERE code = 'it44_admin_co';
            INSERT INTO security.user_role_assignments (tenant_id, user_id, role_id)
              SELECT '{Tenant}', '{AdminOt}', id FROM security.roles WHERE code = 'it44_admin_ot';
            """);
        _rep = await IdAsync(cn, "SELECT id FROM security.permissions WHERE slug = 'it44.reportes.read'");
        _pii = await IdAsync(cn, "SELECT id FROM security.permissions WHERE slug = 'it44.pii.reveal'");
        _off = await IdAsync(cn, "SELECT id FROM security.permissions WHERE slug = 'it44.off'");
        await ExecAsync(cn, $"""
            INSERT INTO security.user_permission_overrides (tenant_id, user_id, product_code, permission_id, effect, granted_by)
            VALUES ('{Tenant}','{AdminOt}','tramites','{_rep}','allow','{Other}');
            """);
        return cn;
    }

    private static OverrideActor Co => new(AdminCo, false);

    private static OverrideActor Ot => new(AdminOt, false);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Task<long> ActiveAsync(NpgsqlConnection cn, Guid user, Guid permission, string effect) => CountAsync(cn, $"""
        SELECT count(*) FROM security.user_permission_overrides
         WHERE tenant_id = '{Tenant}' AND user_id = '{user}' AND permission_id = '{permission}' AND effect = '{effect}' AND deleted_at IS NULL
        """);

    private static Task<long> AuditAsync(NpgsqlConnection cn, string action, string outcome, string? reason) => CountAsync(cn, $"""
        SELECT count(*) FROM security.user_permission_audit
         WHERE tenant_id = '{Tenant}' AND target_user_id = '{Target}' AND action = '{action}' AND outcome = '{outcome}'
           AND reason_code {(reason is null ? "IS NULL" : $"= '{reason}'")}
        """);

    [PostgresFact]
    public async Task AC1_otorga_dentro_del_tope_y_queda_el_permiso_individual()
    {
        await using var cn = await SeedAsync();

        var r = await Handler().SetAsync(Tenant, Co, Target, _rep, "allow", Ct);

        r.Changed.Should().BeTrue();
        (await ActiveAsync(cn, Target, _rep, "allow")).Should().Be(1);
        (await CountAsync(cn, $"SELECT count(*) FROM security.user_permission_overrides WHERE user_id = '{Target}' AND granted_by = '{AdminCo}'")).Should().Be(1);
        (await AuditAsync(cn, "GRANT", "OK", null)).Should().Be(1);
    }

    [PostgresFact]
    public async Task AC2_fuera_del_tope_rechaza_y_el_intento_queda_auditado_como_REJECTED()
    {
        await using var cn = await SeedAsync();

        var act = async () => await Handler().SetAsync(Tenant, Co, Target, _pii, "allow", Ct);

        (await act.Should().ThrowAsync<PermissionOverrideRejectedException>()).Which.ReasonCode.Should().Be("OUT_OF_CAP");
        (await ActiveAsync(cn, Target, _pii, "allow")).Should().Be(0);
        (await AuditAsync(cn, "GRANT", "REJECTED", "OUT_OF_CAP")).Should().Be(1);
    }

    [PostgresFact]
    public async Task AC3_el_allow_individual_del_admin_no_amplia_su_tope()
    {
        await using var cn = await SeedAsync();

        var act = async () => await Handler().SetAsync(Tenant, Ot, Target, _rep, "allow", Ct);

        await act.Should().ThrowAsync<PermissionOverrideRejectedException>();
        (await ActiveAsync(cn, Target, _rep, "allow")).Should().Be(0);
    }

    [PostgresFact]
    public async Task AC4_quitar_fuera_del_tope_se_puede_y_volver_a_habilitarlo_da_403()
    {
        await using var cn = await SeedAsync();
        await ExecAsync(cn, $"""
            INSERT INTO security.user_permission_overrides (tenant_id, user_id, product_code, permission_id, effect, granted_by)
            VALUES ('{Tenant}','{Target}','tramites','{_pii}','allow','{Other}');
            """);

        (await Handler().RemoveAsync(Tenant, Co, Target, _pii, Ct)).Should().BeTrue();

        (await ActiveAsync(cn, Target, _pii, "allow")).Should().Be(0);
        (await CountAsync(cn, $"SELECT count(*) FROM security.user_permission_overrides WHERE user_id = '{Target}' AND deleted_at IS NOT NULL AND deleted_by = '{AdminCo}'")).Should().Be(1);
        (await AuditAsync(cn, "REVOKE", "OK", null)).Should().Be(1);
        var again = async () => await Handler().SetAsync(Tenant, Co, Target, _pii, "allow", Ct);
        await again.Should().ThrowAsync<PermissionOverrideRejectedException>();
    }

    [PostgresFact]
    public async Task Deny_sobre_un_allow_da_de_baja_el_anterior_y_deja_el_deny_vigente()
    {
        await using var cn = await SeedAsync();
        await Handler().SetAsync(Tenant, Co, Target, _rep, "allow", Ct);

        await Handler().SetAsync(Tenant, Co, Target, _rep, "deny", Ct);

        (await ActiveAsync(cn, Target, _rep, "deny")).Should().Be(1);
        (await ActiveAsync(cn, Target, _rep, "allow")).Should().Be(0);
        (await CountAsync(cn, $"SELECT count(*) FROM security.user_permission_audit WHERE action = 'DENY' AND value_before = 'allow' AND value_after = 'deny' AND target_user_id = '{Target}'")).Should().Be(1);
    }

    [PostgresFact]
    public async Task AC5_catalogo_otorgable_super_admin_ve_todo_enforced_true_y_el_admin_solo_su_tope()
    {
        await using var cn = await SeedAsync();
        var enforced = await CountAsync(cn, "SELECT count(*) FROM security.permissions WHERE enforced = 'true' AND is_active AND deleted_at IS NULL");

        var sa = await Handler().ListGrantableAsync(Tenant, new OverrideActor(SuperAdmin, true), Ct);
        var co = await Handler().ListGrantableAsync(Tenant, Co, Ct);

        sa.Count.Should().Be((int)enforced);
        sa.Select(p => p.Slug).Should().Contain(["it44.reportes.read", "it44.pii.reveal"]).And.NotContain("it44.off");
        co.Select(p => p.Slug).Should().Equal("it44.reportes.read");
        sa.Should().OnlyContain(p => p.Id != _off);
    }

    [PostgresFact]
    public async Task Permiso_no_enforced_no_es_otorgable_ni_para_el_super_admin()
    {
        await using var cn = await SeedAsync();

        var act = async () => await Handler().SetAsync(Tenant, new OverrideActor(SuperAdmin, true), Target, _off, "allow", Ct);

        (await act.Should().ThrowAsync<PermissionOverrideRejectedException>()).Which.ReasonCode.Should().Be("NOT_ENFORCED");
        (await AuditAsync(cn, "GRANT", "REJECTED", "NOT_ENFORCED")).Should().Be(1);
    }

    [PostgresFact]
    public async Task AC6_si_el_admin_pierde_el_permiso_lo_que_otorgo_sigue_vigente()
    {
        await using var cn = await SeedAsync();
        await Handler().SetAsync(Tenant, Co, Target, _rep, "allow", Ct);

        await ExecAsync(cn, $"UPDATE security.user_role_assignments SET deleted_at = now() WHERE user_id = '{AdminCo}'");

        (await Handler().ListGrantableAsync(Tenant, Co, Ct)).Should().BeEmpty("el admin ya no tiene tope");
        (await ActiveAsync(cn, Target, _rep, "allow")).Should().Be(1);
    }
}
