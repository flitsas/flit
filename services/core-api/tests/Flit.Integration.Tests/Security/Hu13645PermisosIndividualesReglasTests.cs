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
/// HU #13645 (Feature #13101 F-B, Epica #12753) contra PostgreSQL real, con las reglas reales y el directorio real:
/// ambito (compania hija y otra compania, AC5), invitacion pendiente (AC6), anti-fuga entre tenants en lectura y
/// escritura (AC7, la app conecta como owner y la RLS no aplica: el aislamiento sale del codigo), mas AC1 a AC4.
/// </summary>
public sealed class Hu13645PermisosIndividualesReglasTests(PostgresDatabaseFixture fixture) : PostgresTestBase(fixture)
{
    private static readonly Guid TenantA = new("aaaaaaaa-1364-4500-8000-00000000000a");
    private static readonly Guid TenantChild = new("aaaaaaaa-1364-4500-8000-00000000000c");
    private static readonly Guid TenantB = new("bbbbbbbb-1364-4500-8000-00000000000b");
    private static readonly Guid ActorA = new("a1000000-1364-4500-8000-000000000001");
    private static readonly Guid UserA = new("a2000000-1364-4500-8000-000000000002");
    private static readonly Guid AdminA = new("a3000000-1364-4500-8000-000000000003");
    private static readonly Guid SuperAdminUser = new("a4000000-1364-4500-8000-000000000004");
    private static readonly Guid ChildUser = new("a5000000-1364-4500-8000-000000000005");
    private static readonly Guid UserB = new("b1000000-1364-4500-8000-000000000001");
    private static readonly Guid PendingUser = new("a6000000-1364-4500-8000-000000000006");
    private static readonly Guid InvitedUser = new("a7000000-1364-4500-8000-000000000007");
    private static readonly Guid Ghost = new("a8000000-1364-4500-8000-000000000008");

    private readonly List<Flit.Infrastructure.Persistence.FlitDbContext> _contexts = [];
    private Guid _rep;
    private Guid _excl;

    public override async ValueTask DisposeAsync()
    {
        foreach (var ctx in _contexts)
        {
            await ctx.DisposeAsync();
        }

        await base.DisposeAsync();
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

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
        var lookup = new OverrideTargetLookup(new OverrideTargetDirectory(ctx));
        IPermissionOverrideRule[] rules =
        [
            new UserExistsRule(lookup), new SelfEditRule(), new TargetSuperAdminRule(lookup), new TenantScopeRule(lookup),
            new TargetIsAdminRule(lookup), new ExcludedPermissionRule(), new InvitationPendingRule(lookup),
        ];
        return new ManageUserPermissionOverridesHandler(repo, new PermissionCapCalculator(repo), new GrantModuleOverridesHandler(repo), rules);
    }

    private static async Task InsertRoleIfMissingAsync(NpgsqlConnection cn, string code) => await ExecAsync(cn, $"""
        INSERT INTO security.roles (code, name, is_system, is_active, target_entity_type, product_code)
          SELECT '{code}', '{code}', false, true, 'COMPANY', 'tramites'
           WHERE NOT EXISTS (SELECT 1 FROM security.roles WHERE code = '{code}' AND deleted_at IS NULL);
        """);

    private static Task AssignAsync(NpgsqlConnection cn, Guid tenant, Guid user, string roleCode) => ExecAsync(cn, $"""
        INSERT INTO security.user_role_assignments (tenant_id, user_id, role_id)
          SELECT '{tenant}', '{user}', id FROM security.roles WHERE code = '{roleCode}' AND deleted_at IS NULL LIMIT 1;
        """);

    // A es cabeza de grupo con una hija; B es otra compania. ActorA tiene reportes.read por un rol; el resto, sin permisos.
    private async Task<NpgsqlConnection> SeedAsync()
    {
        var cn = await Fixture.OpenConnectionAsync();
        await ExecAsync(cn, $"""
            INSERT INTO platform.products (code, name, icon) VALUES ('tramites','Tramites','file') ON CONFLICT (code) DO NOTHING;
            INSERT INTO identity.tenants (id, code, legal_name, tax_id, tenant_type, is_active, is_group_parent, created_at) VALUES
              ('{TenantA}','IT-45-A','A','9136450000001','CONCESION',true,true,now()),
              ('{TenantB}','IT-45-B','B','9136450000002','CONCESIONARIO',true,false,now());
            INSERT INTO identity.tenants (id, code, legal_name, tax_id, tenant_type, is_active, is_group_parent, parent_tenant_id, created_at)
              VALUES ('{TenantChild}','IT-45-C','C','9136450000003','CONCESIONARIO',true,false,'{TenantA}',now());
            INSERT INTO identity.users (id, email, display_name, status, home_tenant_id, created_at) VALUES
              ('{ActorA}','actor13645@it.local','Actor','active','{TenantA}',now()),
              ('{UserA}','ua13645@it.local','UA','active','{TenantA}',now()),
              ('{AdminA}','adm13645@it.local','Adm','active','{TenantA}',now()),
              ('{SuperAdminUser}','sa13645@it.local','Sa','active',NULL,now()),
              ('{ChildUser}','child13645@it.local','Child','active','{TenantChild}',now()),
              ('{UserB}','ub13645@it.local','UB','active','{TenantB}',now()),
              ('{PendingUser}','pend13645@it.local','Pend','pending','{TenantA}',now()),
              ('{InvitedUser}','inv13645@it.local','Inv','active','{TenantA}',now());
            INSERT INTO security.modules (code, name, sort_order, is_active, product_code) VALUES ('it45_m','IT 45',1,true,'tramites');
            INSERT INTO security.permissions (module_id, slug, name, http_method, route_pattern, is_active, enforced)
              SELECT id, v.slug, v.slug, 'GET', '/api/v1/it45/' || v.slug, true, v.enforced
                FROM security.modules, (VALUES ('it45.reportes.read','true'), ('it45.excl','excluded')) AS v(slug, enforced)
               WHERE code = 'it45_m';
            """);
        foreach (var code in new[] { "it45_op", "AdminCompany", "SuperAdmin" })
        {
            await InsertRoleIfMissingAsync(cn, code);
        }

        await ExecAsync(cn, """
            INSERT INTO security.role_permissions (role_id, permission_id)
              SELECT r.id, p.id FROM security.roles r, security.permissions p WHERE r.code = 'it45_op' AND p.slug = 'it45.reportes.read';
            """);
        _rep = await IdAsync(cn, "SELECT id FROM security.permissions WHERE slug = 'it45.reportes.read'");
        _excl = await IdAsync(cn, "SELECT id FROM security.permissions WHERE slug = 'it45.excl'");
        await AssignAsync(cn, TenantA, ActorA, "it45_op");
        await AssignAsync(cn, TenantA, UserA, "it45_op");
        await AssignAsync(cn, TenantA, AdminA, "AdminCompany");
        await AssignAsync(cn, TenantB, SuperAdminUser, "SuperAdmin"); // el rol global vive en otro tenant
        await AssignAsync(cn, TenantChild, ChildUser, "it45_op");
        await AssignAsync(cn, TenantB, UserB, "it45_op");
        await AssignAsync(cn, TenantA, PendingUser, "it45_op");
        await AssignAsync(cn, TenantA, InvitedUser, "it45_op");
        await ExecAsync(cn, $"""
            INSERT INTO security.user_invitations (tenant_id, email, full_name, role_id, token_hash, status, invited_by)
              SELECT '{TenantA}', 'inv13645@it.local', 'Inv', id, 'it45-token-hash', 'pending', '{ActorA}'
                FROM security.roles WHERE code = 'it45_op' AND deleted_at IS NULL LIMIT 1;
            INSERT INTO security.user_permission_overrides (tenant_id, user_id, product_code, permission_id, effect, granted_by)
            VALUES ('{TenantB}','{UserB}','tramites','{_rep}','allow','{UserB}');
            """);
        return cn;
    }

    private static OverrideActor Actor => new(ActorA, false);

    private static OverrideActor Super => new(Guid.NewGuid(), true);

    private static Task<long> ActiveAsync(NpgsqlConnection cn, Guid tenant, Guid user) => CountAsync(cn, $"""
        SELECT count(*) FROM security.user_permission_overrides WHERE tenant_id = '{tenant}' AND user_id = '{user}' AND deleted_at IS NULL
        """);

    private static Task<long> RejectedAsync(NpgsqlConnection cn, Guid tenant, Guid target, string action, string reason) => CountAsync(cn, $"""
        SELECT count(*) FROM security.user_permission_audit
         WHERE tenant_id = '{tenant}' AND target_user_id = '{target}' AND action = '{action}' AND outcome = 'REJECTED' AND reason_code = '{reason}'
        """);

    private static async Task AssertRejectedAsync(Task act, string reason)
    {
        var ex = await FluentActions.Awaiting(() => act).Should().ThrowAsync<PermissionOverrideRejectedException>();
        ex.Which.ReasonCode.Should().Be(reason);
    }

    [PostgresFact]
    public async Task Control_un_usuario_normal_del_tenant_se_configura_y_queda_el_override()
    {
        await using var cn = await SeedAsync();

        (await Handler().SetAsync(TenantA, Actor, UserA, _rep, "allow", Ct)).Changed.Should().BeTrue();

        (await ActiveAsync(cn, TenantA, UserA)).Should().Be(1);
    }

    [PostgresFact]
    public async Task AC1_admin_de_compania_sobre_otro_administrador_da_TARGET_IS_ADMIN_y_el_super_admin_si_puede()
    {
        await using var cn = await SeedAsync();

        await AssertRejectedAsync(Handler().SetAsync(TenantA, Actor, AdminA, _rep, "allow", Ct), "TARGET_IS_ADMIN");
        (await RejectedAsync(cn, TenantA, AdminA, "GRANT", "TARGET_IS_ADMIN")).Should().Be(1);
        (await ActiveAsync(cn, TenantA, AdminA)).Should().Be(0);

        (await Handler().SetAsync(TenantA, Super, AdminA, _rep, "allow", Ct)).Changed.Should().BeTrue();
        (await ActiveAsync(cn, TenantA, AdminA)).Should().Be(1);
    }

    [PostgresFact]
    public async Task AC2_sobre_si_mismo_da_SELF()
    {
        await using var cn = await SeedAsync();

        await AssertRejectedAsync(Handler().SetAsync(TenantA, Actor, ActorA, _rep, "deny", Ct), "SELF");

        (await RejectedAsync(cn, TenantA, ActorA, "DENY", "SELF")).Should().Be(1);
        (await ActiveAsync(cn, TenantA, ActorA)).Should().Be(0);
    }

    [PostgresFact]
    public async Task AC3_destino_Super_Admin_da_TARGET_SUPERADMIN_incluso_para_el_Super_Admin_aunque_su_rol_viva_en_otro_tenant()
    {
        await using var cn = await SeedAsync();

        await AssertRejectedAsync(Handler().SetAsync(TenantB, Super, SuperAdminUser, _rep, "allow", Ct), "TARGET_SUPERADMIN");
        await AssertRejectedAsync(Handler().SetAsync(TenantA, Actor, SuperAdminUser, _rep, "allow", Ct), "TARGET_SUPERADMIN");

        (await ActiveAsync(cn, TenantB, SuperAdminUser)).Should().Be(0);
        (await ActiveAsync(cn, TenantA, SuperAdminUser)).Should().Be(0);
    }

    [PostgresFact]
    public async Task AC4_excluidos_dan_EXCLUDED_en_allow_deny_quitar_y_modulo_completo()
    {
        await using var cn = await SeedAsync();

        await AssertRejectedAsync(Handler().SetAsync(TenantA, Actor, UserA, _excl, "allow", Ct), "EXCLUDED");
        await AssertRejectedAsync(Handler().SetAsync(TenantA, Super, UserA, _excl, "deny", Ct), "EXCLUDED");
        await AssertRejectedAsync(Handler().RemoveAsync(TenantA, Actor, UserA, _excl, Ct), "EXCLUDED");

        // El modulo it45_m mezcla una accion otorgable y una excluida: se otorga solo la otorgable.
        var r = await Handler().GrantModuleAsync(TenantA, Actor, UserA, "it45_m", "deny", Ct);
        r.Created.Should().Be(1);
        r.Excluded.Should().Be(1);
        (await ActiveAsync(cn, TenantA, UserA)).Should().Be(1);
        (await CountAsync(cn, $"SELECT count(*) FROM security.user_permission_overrides WHERE user_id = '{UserA}' AND permission_id = '{_excl}'")).Should().Be(0);
    }

    [PostgresFact]
    public async Task AC5_usuario_de_compania_hija_o_de_otra_compania_da_OUT_OF_SCOPE_y_no_escribe()
    {
        await using var cn = await SeedAsync();

        await AssertRejectedAsync(Handler().SetAsync(TenantA, Actor, ChildUser, _rep, "allow", Ct), "OUT_OF_SCOPE");
        await AssertRejectedAsync(Handler().SetAsync(TenantA, Actor, UserB, _rep, "allow", Ct), "OUT_OF_SCOPE");
        await AssertRejectedAsync(Handler().RemoveAsync(TenantA, Actor, UserB, _rep, Ct), "OUT_OF_SCOPE");
        await AssertRejectedAsync(Handler().GrantModuleAsync(TenantA, Actor, ChildUser, "it45_m", "deny", Ct), "OUT_OF_SCOPE");

        (await ActiveAsync(cn, TenantA, ChildUser)).Should().Be(0);
        (await ActiveAsync(cn, TenantA, UserB)).Should().Be(0);
        (await RejectedAsync(cn, TenantA, ChildUser, "GRANT", "OUT_OF_SCOPE")).Should().Be(1);
    }

    [PostgresFact]
    public async Task AC5_el_Super_Admin_configura_a_la_hija_declarando_su_tenant_pero_no_a_un_usuario_de_otro_tenant()
    {
        await using var cn = await SeedAsync();

        (await Handler().SetAsync(TenantChild, Super, ChildUser, _rep, "allow", Ct)).Changed.Should().BeTrue();
        await AssertRejectedAsync(Handler().SetAsync(TenantChild, Super, UserB, _rep, "allow", Ct), "OUT_OF_SCOPE");

        (await ActiveAsync(cn, TenantChild, ChildUser)).Should().Be(1);
        (await ActiveAsync(cn, TenantChild, UserB)).Should().Be(0);
    }

    [PostgresFact]
    public async Task AC6_usuario_con_invitacion_pendiente_da_INVITATION_PENDING_por_estado_o_por_invitacion_vigente()
    {
        await using var cn = await SeedAsync();

        await AssertRejectedAsync(Handler().SetAsync(TenantA, Actor, PendingUser, _rep, "allow", Ct), "INVITATION_PENDING");
        await AssertRejectedAsync(Handler().SetAsync(TenantA, Actor, InvitedUser, _rep, "allow", Ct), "INVITATION_PENDING");

        (await ActiveAsync(cn, TenantA, PendingUser)).Should().Be(0);
        (await ActiveAsync(cn, TenantA, InvitedUser)).Should().Be(0);
        (await RejectedAsync(cn, TenantA, PendingUser, "GRANT", "INVITATION_PENDING")).Should().Be(1);
    }

    [PostgresFact]
    public async Task AC7_anti_fuga_escritura_un_admin_del_tenant_A_no_toca_al_tenant_B_declarandolo()
    {
        await using var cn = await SeedAsync();
        var before = await ActiveAsync(cn, TenantB, UserB);

        // El actor es de A pero declara el tenant B: no es miembro de B (y UserB si lo es), asi que se rechaza.
        await AssertRejectedAsync(Handler().SetAsync(TenantB, Actor, UserB, _rep, "deny", Ct), "OUT_OF_SCOPE");
        await AssertRejectedAsync(Handler().RemoveAsync(TenantB, Actor, UserB, _rep, Ct), "OUT_OF_SCOPE");
        await AssertRejectedAsync(Handler().GrantModuleAsync(TenantB, Actor, UserB, "it45_m", "allow", Ct), "OUT_OF_SCOPE");

        (await ActiveAsync(cn, TenantB, UserB)).Should().Be(before);
        (await CountAsync(cn, $"SELECT count(*) FROM security.user_permission_overrides WHERE tenant_id = '{TenantB}' AND effect = 'deny'")).Should().Be(0);
        (await RejectedAsync(cn, TenantB, UserB, "GRANT", "OUT_OF_SCOPE")).Should().Be(0);
        (await RejectedAsync(cn, TenantB, UserB, "DENY", "OUT_OF_SCOPE")).Should().Be(1);
    }

    [PostgresFact]
    public async Task AC7_anti_fuga_lectura_no_se_listan_los_overrides_de_otro_tenant_ni_de_un_usuario_ajeno()
    {
        await using var cn = await SeedAsync();

        // Con su propio tenant, un usuario de B no se ve (aunque tenga overrides en B).
        await AssertRejectedAsync(Handler().ListAsync(TenantA, Actor, UserB, Ct), "OUT_OF_SCOPE");
        // Declarando B: el actor no es miembro de B.
        await AssertRejectedAsync(Handler().ListAsync(TenantB, Actor, UserB, Ct), "OUT_OF_SCOPE");
        (await RejectedAsync(cn, TenantA, UserB, "READ", "OUT_OF_SCOPE")).Should().Be(1);

        // El Super Admin si lee, pero solo declarando el tenant correcto.
        (await Handler().ListAsync(TenantB, Super, UserB, Ct)).Should().ContainSingle(i => i.Slug == "it45.reportes.read");
        await AssertRejectedAsync(Handler().ListAsync(TenantA, Super, UserB, Ct), "OUT_OF_SCOPE");
    }

    [PostgresFact]
    public async Task Usuario_inexistente_da_USER_NOT_FOUND_sin_insert_ni_error_de_FK()
    {
        await using var cn = await SeedAsync();

        await AssertRejectedAsync(Handler().SetAsync(TenantA, Actor, Ghost, _rep, "allow", Ct), "USER_NOT_FOUND");

        (await ActiveAsync(cn, TenantA, Ghost)).Should().Be(0);
        (await RejectedAsync(cn, TenantA, Ghost, "GRANT", "USER_NOT_FOUND")).Should().Be(1);
    }
}
