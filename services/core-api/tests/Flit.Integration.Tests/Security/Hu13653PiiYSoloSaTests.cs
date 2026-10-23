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
/// HU #13653 (Feature #13103 F-D1, Épica #12753) contra PostgreSQL real: regla «Solo SA» (permisos que ningún administrador
/// del tenant destino tiene en su tope) y confirmación Ley 1581 de <c>ict.pii.reveal</c> con su fila PII_REVEAL_CONFIRMED (FB-17).
/// </summary>
public sealed class Hu13653PiiYSoloSaTests(PostgresDatabaseFixture fixture) : PostgresTestBase(fixture)
{
    private static readonly Guid Tenant = new("aaaaaaaa-1365-4400-8000-00000000000a");
    private static readonly Guid AdminCo = new("a1000000-1365-4400-8000-000000000001");
    private static readonly Guid Target = new("a3000000-1365-4400-8000-000000000003");
    private static readonly Guid SuperAdmin = new("a4000000-1365-4400-8000-000000000004");

    private readonly List<Flit.Infrastructure.Persistence.FlitDbContext> _contexts = [];
    private Guid _rep;
    private Guid _saOnly;
    private Guid _pii;
    private string _piiModule = "it53_m";

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

    private static async Task<object?> ScalarAsync(NpgsqlConnection cn, string sql)
    {
        await using var cmd = new NpgsqlCommand(sql, cn);
        return await cmd.ExecuteScalarAsync(Ct);
    }

    private ManageUserPermissionOverridesHandler Handler()
    {
        var ctx = NewContext();
        _contexts.Add(ctx);
        var repo = new UserPermissionOverrideRepository(ctx);
        return new ManageUserPermissionOverridesHandler(repo, new PermissionCapCalculator(repo), new GrantModuleOverridesHandler(repo), []);
    }

    private async Task<NpgsqlConnection> SeedAsync()
    {
        var cn = await Fixture.OpenConnectionAsync();
        await ExecAsync(cn, $"""
            INSERT INTO platform.products (code, name, icon) VALUES ('tramites','Tramites','file') ON CONFLICT (code) DO NOTHING;
            INSERT INTO identity.tenants (id, code, legal_name, tax_id, tenant_type, is_active, is_group_parent, created_at)
              VALUES ('{Tenant}','IT-53-A','A','9136530000001','CONCESIONARIO',true,false,now());
            INSERT INTO identity.users (id, email, display_name, status, created_at) VALUES
              ('{AdminCo}','co13653@it.local','Co','active',now()), ('{Target}','t13653@it.local','T','active',now()),
              ('{SuperAdmin}','sa13653@it.local','Sa','active',now());
            INSERT INTO security.modules (code, name, sort_order, is_active, product_code) VALUES ('it53_m','IT 53',1,true,'tramites');
            INSERT INTO security.permissions (module_id, slug, name, http_method, route_pattern, is_active, enforced)
              SELECT id, v.slug, v.slug, 'GET', '/api/v1/it53/' || v.slug, true, 'true'
                FROM security.modules, (VALUES ('it53.reportes.read'), ('it53.solo.sa')) AS v(slug) WHERE code = 'it53_m';
            INSERT INTO security.roles (code, name, is_system, is_active, target_entity_type, product_code)
              SELECT 'AdminCompany','Admin compania',false,true,'COMPANY','tramites'
               WHERE NOT EXISTS (SELECT 1 FROM security.roles WHERE code = 'AdminCompany');
            INSERT INTO security.role_permissions (role_id, permission_id)
              SELECT r.id, p.id FROM security.roles r, security.permissions p
               WHERE r.code = 'AdminCompany' AND p.slug = 'it53.reportes.read'
                 AND NOT EXISTS (SELECT 1 FROM security.role_permissions x WHERE x.role_id = r.id AND x.permission_id = p.id);
            INSERT INTO security.user_role_assignments (tenant_id, user_id, role_id)
              SELECT '{Tenant}', '{AdminCo}', id FROM security.roles WHERE code = 'AdminCompany';
            """);
        if (await ScalarAsync(cn, "SELECT id FROM security.permissions WHERE slug = 'ict.pii.reveal'") is null)
        {
            await ExecAsync(cn, """
                INSERT INTO security.permissions (module_id, slug, name, http_method, route_pattern, is_active, enforced)
                  SELECT id, 'ict.pii.reveal', 'ict.pii.reveal', 'POST', '/api/v1/it53/pii', true, 'true'
                    FROM security.modules WHERE code = 'it53_m';
                """);
        }

        _rep = (Guid)(await ScalarAsync(cn, "SELECT id FROM security.permissions WHERE slug = 'it53.reportes.read'"))!;
        _saOnly = (Guid)(await ScalarAsync(cn, "SELECT id FROM security.permissions WHERE slug = 'it53.solo.sa'"))!;
        _pii = (Guid)(await ScalarAsync(cn, "SELECT id FROM security.permissions WHERE slug = 'ict.pii.reveal'"))!;
        _piiModule = (string)(await ScalarAsync(cn, "SELECT m.code FROM security.permissions p JOIN security.modules m ON m.id = p.module_id WHERE p.slug = 'ict.pii.reveal'"))!;
        await ExecAsync(cn, "UPDATE security.permissions SET enforced = 'true' WHERE slug = 'ict.pii.reveal'");
        return cn;
    }

    private static OverrideActor Sa => new(SuperAdmin, true);

    private static async Task<long> CountAsync(NpgsqlConnection cn, string sql) => Convert.ToInt64(await ScalarAsync(cn, sql));

    private static Task<long> AuditAsync(NpgsqlConnection cn, string action) => CountAsync(cn, $"""
        SELECT count(*) FROM security.user_permission_audit
         WHERE tenant_id = '{Tenant}' AND target_user_id = '{Target}' AND action = '{action}' AND outcome = 'OK'
           AND permission_slug = 'ict.pii.reveal'
        """);

    private static Task<long> ActivePiiAsync(NpgsqlConnection cn, Guid pii) => CountAsync(cn, $"""
        SELECT count(*) FROM security.user_permission_overrides
         WHERE tenant_id = '{Tenant}' AND user_id = '{Target}' AND permission_id = '{pii}' AND effect = 'allow' AND deleted_at IS NULL
        """);

    [PostgresFact]
    public async Task AC1_solo_SA_es_lo_que_ningun_administrador_del_tenant_destino_tiene_en_su_tope()
    {
        await using var cn = await SeedAsync();
        var handler = Handler();
        var grantable = await handler.ListGrantableAsync(Tenant, Sa, Ct);

        var onlySa = await handler.ListSuperAdminOnlyAsync(Tenant, grantable, Ct);

        onlySa.Should().Contain(_saOnly).And.Contain(_pii).And.NotContain(_rep);
    }

    [PostgresFact]
    public async Task AC1_un_deny_individual_del_unico_administrador_vuelve_el_permiso_solo_SA()
    {
        await using var cn = await SeedAsync();
        await ExecAsync(cn, $"""
            INSERT INTO security.user_permission_overrides (tenant_id, user_id, product_code, permission_id, effect, granted_by)
            VALUES ('{Tenant}','{AdminCo}','tramites','{_rep}','deny','{SuperAdmin}');
            """);
        var handler = Handler();

        var onlySa = await handler.ListSuperAdminOnlyAsync(Tenant, await handler.ListGrantableAsync(Tenant, Sa, Ct), Ct);

        onlySa.Should().Contain(_rep);
    }

    [PostgresFact]
    public async Task AC2_sin_confirmar_ict_pii_reveal_lanza_y_no_cambia_nada()
    {
        await using var cn = await SeedAsync();

        var act = async () => await Handler().SetAsync(Tenant, Sa, Target, _pii, "allow", Ct);

        await act.Should().ThrowAsync<PiiConfirmationRequiredException>();
        (await ActivePiiAsync(cn, _pii)).Should().Be(0);
        (await AuditAsync(cn, "GRANT")).Should().Be(0);
        (await AuditAsync(cn, "PII_REVEAL_CONFIRMED")).Should().Be(0);
    }

    [PostgresFact]
    public async Task AC2_confirmado_guarda_el_permiso_y_audita_GRANT_y_PII_REVEAL_CONFIRMED()
    {
        await using var cn = await SeedAsync();

        var r = await Handler().SetAsync(Tenant, Sa, Target, _pii, "allow", Ct, piiConfirmed: true);

        r.Changed.Should().BeTrue();
        (await ActivePiiAsync(cn, _pii)).Should().Be(1);
        (await AuditAsync(cn, "GRANT")).Should().Be(1);
        (await AuditAsync(cn, "PII_REVEAL_CONFIRMED")).Should().Be(1);
        (await CountAsync(cn, $"""
            SELECT count(*) FROM security.user_permission_audit
             WHERE action = 'PII_REVEAL_CONFIRMED' AND actor_user_id = '{SuperAdmin}' AND value_after = 'allow'
            """)).Should().Be(1);
    }

    [PostgresFact]
    public async Task AC2_denegar_ict_pii_reveal_no_pide_confirmacion()
    {
        await using var cn = await SeedAsync();

        await Handler().SetAsync(Tenant, Sa, Target, _pii, "deny", Ct);

        (await AuditAsync(cn, "DENY")).Should().Be(1);
        (await AuditAsync(cn, "PII_REVEAL_CONFIRMED")).Should().Be(0);
    }

    [PostgresFact]
    public async Task AC2_el_modulo_completo_no_otorga_ict_pii_reveal()
    {
        await using var cn = await SeedAsync();

        var r = await Handler().GrantModuleAsync(Tenant, Sa, Target, _piiModule, "allow", Ct);

        r.Excluded.Should().BeGreaterThanOrEqualTo(1);
        (await ActivePiiAsync(cn, _pii)).Should().Be(0);
        (await AuditAsync(cn, "PII_REVEAL_CONFIRMED")).Should().Be(0);
    }
}
