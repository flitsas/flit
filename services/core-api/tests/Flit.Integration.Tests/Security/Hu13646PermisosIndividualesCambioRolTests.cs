using Flit.Infrastructure.Persistence.Repositories;
using Flit.Integration.Tests.Postgres;
using Flit.Modules.Security.Application.PermissionOverrides;
using Flit.Modules.Security.Application.PermissionOverrides.GrantModule;
using Flit.Modules.Security.Application.UserRoles;
using Flit.Modules.Security.Domain.PermissionOverrides;
using Flit.Modules.Security.Domain.UserRoles;
using FluentAssertions;
using Npgsql;
using Xunit;

namespace Flit.Integration.Tests.Security;

/// <summary>
/// HU #13646 (Feature #13101 F-B, Epica #12753, FB-15) contra PostgreSQL real: al cambiar el rol se eliminan los permisos
/// individuales del tenant y del producto del rol nuevo (AC1), el conteo previo (AC2), otro producto intacto (AC3) y
/// un cambio que falla no purga (misma transaccion).
/// </summary>
public sealed class Hu13646PermisosIndividualesCambioRolTests(PostgresDatabaseFixture fixture) : PostgresTestBase(fixture)
{
    private static readonly Guid Tenant = new("aaaaaaaa-1364-4600-8000-00000000000a");
    private static readonly Guid Actor = new("a1000000-1364-4600-8000-000000000001");
    private static readonly Guid Target = new("a2000000-1364-4600-8000-000000000002");

    private readonly List<Flit.Infrastructure.Persistence.FlitDbContext> _contexts = [];
    private Guid _roleNew;
    private Guid _roleOld;

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

    private Flit.Infrastructure.Persistence.FlitDbContext Ctx()
    {
        var ctx = NewContext();
        _contexts.Add(ctx);
        return ctx;
    }

    private AssignRoleHandler AssignHandler(Func<IUserRoleAssignmentRepository, IUserRoleAssignmentRepository>? decorate = null)
    {
        var ctx = Ctx();
        IUserRoleAssignmentRepository repo = new UserRoleAssignmentRepository(ctx);
        return new AssignRoleHandler(decorate?.Invoke(repo) ?? repo, new RoleChangeOverridesPurger(ctx));
    }

    private ManageUserPermissionOverridesHandler ManageHandler()
    {
        var ctx = Ctx();
        var repo = new UserPermissionOverrideRepository(ctx);
        var lookup = new OverrideTargetLookup(new OverrideTargetDirectory(ctx));
        IPermissionOverrideRule[] rules = [new UserExistsRule(lookup), new TenantScopeRule(lookup)];
        return new ManageUserPermissionOverridesHandler(repo, new PermissionCapCalculator(repo), new GrantModuleOverridesHandler(repo), rules);
    }

    // Target con rol it46_old (tramites), 3 permisos individuales en tramites y 1 en comparendos (producto sembrado por la migracion; uno propio quedaria en platform.products, que el reset no trunca).
    private async Task<NpgsqlConnection> SeedAsync()
    {
        var cn = await Fixture.OpenConnectionAsync();
        await ExecAsync(cn, $"""
            INSERT INTO platform.products (code, name, icon) VALUES ('tramites','Tramites','file') ON CONFLICT (code) DO NOTHING;
            INSERT INTO identity.tenants (id, code, legal_name, tax_id, tenant_type, is_active, is_group_parent, created_at)
              VALUES ('{Tenant}','IT-46-A','A','9136460000001','CONCESIONARIO',true,false,now());
            INSERT INTO identity.users (id, email, display_name, status, home_tenant_id, created_at) VALUES
              ('{Actor}','actor13646@it.local','Actor','active','{Tenant}',now()),
              ('{Target}','t13646@it.local','T','active','{Tenant}',now());
            INSERT INTO security.modules (code, name, sort_order, is_active, product_code) VALUES
              ('it46_m','IT 46',1,true,'tramites'), ('it46_o','IT 46 otro',2,true,'comparendos');
            INSERT INTO security.permissions (module_id, slug, name, http_method, route_pattern, is_active, enforced)
              SELECT m.id, v.slug, v.slug, 'GET', '/api/v1/it46/' || v.slug, true, 'true'
                FROM security.modules m JOIN (VALUES ('it46_m','it46.a'),('it46_m','it46.b'),('it46_m','it46.c'),('it46_o','it46.otro')) AS v(mod, slug)
                  ON v.mod = m.code;
            INSERT INTO security.roles (code, name, is_system, is_active, target_entity_type, product_code) VALUES
              ('it46_old','Old',false,true,'COMPANY','tramites'), ('it46_new','New',false,true,'COMPANY','tramites');
            INSERT INTO security.user_role_assignments (tenant_id, user_id, role_id)
              SELECT '{Tenant}', '{Target}', id FROM security.roles WHERE code = 'it46_old';
            INSERT INTO security.user_permission_overrides (tenant_id, user_id, product_code, permission_id, effect, granted_by)
              SELECT '{Tenant}', '{Target}', m.product_code, p.id, 'allow', '{Actor}'
                FROM security.permissions p JOIN security.modules m ON m.id = p.module_id WHERE p.slug LIKE 'it46.%';
            """);
        _roleNew = await IdAsync(cn, "SELECT id FROM security.roles WHERE code = 'it46_new'");
        _roleOld = await IdAsync(cn, "SELECT id FROM security.roles WHERE code = 'it46_old'");
        return cn;
    }

    private static Task<long> ActiveAsync(NpgsqlConnection cn, string product) => CountAsync(cn, $"""
        SELECT count(*) FROM security.user_permission_overrides
         WHERE tenant_id = '{Tenant}' AND user_id = '{Target}' AND product_code = '{product}' AND deleted_at IS NULL
        """);

    private static Task<long> ActiveAssignmentsAsync(NpgsqlConnection cn, Guid roleId) => CountAsync(cn, $"""
        SELECT count(*) FROM security.user_role_assignments
         WHERE tenant_id = '{Tenant}' AND user_id = '{Target}' AND role_id = '{roleId}' AND deleted_at IS NULL
        """);

    private static Task<long> PurgeAuditAsync(NpgsqlConnection cn) => CountAsync(cn, $"""
        SELECT count(*) FROM security.user_permission_audit WHERE tenant_id = '{Tenant}' AND action = 'ROLE_CHANGE_PURGE'
        """);

    [PostgresFact]
    public async Task AC1_cambiar_el_rol_elimina_los_tres_permisos_del_producto_y_deja_una_fila_de_bitacora_por_permiso()
    {
        await using var cn = await SeedAsync();
        (await ActiveAsync(cn, "tramites")).Should().Be(3);

        await AssignHandler().HandleAsync(new AssignRoleCommand(Tenant, Target, _roleNew, Actor), Ct);

        (await ActiveAsync(cn, "tramites")).Should().Be(0);
        (await CountAsync(cn, $"SELECT count(*) FROM security.user_permission_overrides WHERE user_id = '{Target}' AND product_code = 'tramites' AND deleted_at IS NOT NULL AND deleted_by = '{Actor}'"))
            .Should().Be(3);
        (await PurgeAuditAsync(cn)).Should().Be(3);
        (await CountAsync(cn, $"""
            SELECT count(*) FROM security.user_permission_audit
             WHERE action = 'ROLE_CHANGE_PURGE' AND outcome = 'OK' AND actor_user_id = '{Actor}' AND target_user_id = '{Target}'
               AND tenant_id = '{Tenant}' AND product_code = 'tramites' AND value_before = 'allow' AND value_after IS NULL
               AND permission_slug IN ('it46.a','it46.b','it46.c') AND batch_id IS NOT NULL
            """)).Should().Be(3);
        (await CountAsync(cn, "SELECT count(DISTINCT batch_id) FROM security.user_permission_audit WHERE action = 'ROLE_CHANGE_PURGE'"))
            .Should().Be(1);
        (await ActiveAssignmentsAsync(cn, _roleNew)).Should().Be(1);
    }

    // Hallazgo de la prueba funcional (Admin OT): «Heredado del rol» sale de la API con todos los roles del usuario, con las mismas
    // reglas de lectura que el listado de permisos individuales.
    [PostgresFact]
    public async Task Los_permisos_heredados_del_rol_salen_de_todos_sus_roles_y_respetan_el_ambito()
    {
        await using var cn = await SeedAsync();
        await ExecAsync(cn, """
            INSERT INTO security.role_permissions (role_id, permission_id)
              SELECT r.id, p.id FROM security.roles r, security.permissions p WHERE r.code = 'it46_old' AND p.slug IN ('it46.a', 'it46.b');
            """);
        var actor = new OverrideActor(Actor, false);

        var ids = await ManageHandler().ListRolePermissionIdsAsync(Tenant, actor, Target, Ct);

        ids.Should().BeEquivalentTo([
            await IdAsync(cn, "SELECT id FROM security.permissions WHERE slug = 'it46.a'"),
            await IdAsync(cn, "SELECT id FROM security.permissions WHERE slug = 'it46.b'"),
        ]);
        await FluentActions.Awaiting(() => ManageHandler().ListRolePermissionIdsAsync(Tenant, actor, Guid.NewGuid(), Ct))
            .Should().ThrowAsync<PermissionOverrideRejectedException>();
    }

    [PostgresFact]
    public async Task AC2_el_conteo_previo_devuelve_N_por_producto_y_respeta_existencia_y_ambito()
    {
        await using var cn = await SeedAsync();
        var actor = new OverrideActor(Actor, false);

        (await ManageHandler().CountAsync(Tenant, actor, Target, "tramites", Ct)).Should().Be(3);
        (await ManageHandler().CountAsync(Tenant, actor, Target, "comparendos", Ct)).Should().Be(1);
        (await ManageHandler().CountAsync(Tenant, actor, Target, null, Ct)).Should().Be(4);

        await FluentActions.Awaiting(() => ManageHandler().CountAsync(Tenant, actor, Guid.NewGuid(), "tramites", Ct))
            .Should().ThrowAsync<PermissionOverrideRejectedException>();
    }

    // Hallazgo de la prueba funcional (Radicador → AdminCompany): el rol nuevo es de otro producto y el reemplazo cierra el de
    // tramites. El conteo previo por rol anticipa lo que se purga, y se purgan los dos productos.
    [PostgresFact]
    public async Task Cambio_a_un_rol_de_otro_producto_cuenta_y_purga_tambien_el_producto_del_rol_que_se_cierra()
    {
        await using var cn = await SeedAsync();
        await ExecAsync(cn, "INSERT INTO security.roles (code, name, is_system, is_active, target_entity_type, product_code) VALUES ('it46_otrorol','Otro rol',false,true,'COMPANY','comparendos')");
        var otroRol = await IdAsync(cn, "SELECT id FROM security.roles WHERE code = 'it46_otrorol'");
        await using var ctx = Ctx();

        (await ManageHandler().CountForRoleChangeAsync(Tenant, new OverrideActor(Actor, false), Target, otroRol, new UserRoleAssignmentRepository(ctx), Ct))
            .Should().Be(4, "3 de tramites (el rol que se cierra) + 1 de comparendos (el rol nuevo)");

        await AssignHandler().HandleAsync(new AssignRoleCommand(Tenant, Target, otroRol, Actor), Ct);

        (await ActiveAsync(cn, "tramites")).Should().Be(0);
        (await ActiveAsync(cn, "comparendos")).Should().Be(0);
        (await PurgeAuditAsync(cn)).Should().Be(4);
    }

    [PostgresFact]
    public async Task AC3_los_permisos_de_otro_producto_se_conservan_al_cambiar_el_rol_en_tramites()
    {
        await using var cn = await SeedAsync();

        await AssignHandler().HandleAsync(new AssignRoleCommand(Tenant, Target, _roleNew, Actor), Ct);

        (await ActiveAsync(cn, "tramites")).Should().Be(0);
        (await ActiveAsync(cn, "comparendos")).Should().Be(1);
    }

    [PostgresFact]
    public async Task Cambio_fallido_no_purga_ni_cierra_el_rol_anterior()
    {
        await using var cn = await SeedAsync();
        var handler = AssignHandler(inner => new FailingCreateRepository(inner));

        await FluentActions.Awaiting(() => handler.HandleAsync(new AssignRoleCommand(Tenant, Target, _roleNew, Actor), Ct))
            .Should().ThrowAsync<InvalidOperationException>();

        (await ActiveAsync(cn, "tramites")).Should().Be(3);
        (await PurgeAuditAsync(cn)).Should().Be(0);
        (await ActiveAssignmentsAsync(cn, _roleOld)).Should().Be(1);
    }

    [PostgresFact]
    public async Task Rol_igual_al_actual_no_es_cambio_y_no_purga()
    {
        await using var cn = await SeedAsync();

        await FluentActions.Awaiting(() => AssignHandler().HandleAsync(new AssignRoleCommand(Tenant, Target, _roleOld, Actor), Ct))
            .Should().ThrowAsync<RoleAlreadyAssignedException>();

        (await ActiveAsync(cn, "tramites")).Should().Be(3);
        (await PurgeAuditAsync(cn)).Should().Be(0);
    }

    private RemoveRoleAssignmentHandler RemoveHandler(Func<IUserRoleAssignmentRepository, IUserRoleAssignmentRepository>? decorate = null)
    {
        var ctx = Ctx();
        IUserRoleAssignmentRepository repo = new UserRoleAssignmentRepository(ctx);
        return new RemoveRoleAssignmentHandler(decorate?.Invoke(repo) ?? repo, new RoleChangeOverridesPurger(ctx));
    }

    // Hallazgo de review: quitar el rol purga los overrides del producto del rol quitado (antes quedaban vivos y seguian sumando al token).
    // Nota: el indice unico (user_id, tenant_id) impide hoy dos roles activos en un tenant, asi que la rama "queda otro rol del mismo
    // producto" (sin purga) solo la ejercen datos previos al rol unico; su cobertura es unitaria.
    [PostgresFact]
    public async Task Quitar_el_rol_elimina_los_permisos_del_producto_con_bitacora_y_conserva_otro_producto()
    {
        await using var cn = await SeedAsync();

        await RemoveHandler().HandleAsync(Target, Tenant, _roleOld, Actor, Ct);

        (await ActiveAssignmentsAsync(cn, _roleOld)).Should().Be(0);
        (await ActiveAsync(cn, "tramites")).Should().Be(0);
        (await ActiveAsync(cn, "comparendos")).Should().Be(1);
        (await PurgeAuditAsync(cn)).Should().Be(3);
        (await CountAsync(cn, "SELECT count(DISTINCT batch_id) FROM security.user_permission_audit WHERE action = 'ROLE_CHANGE_PURGE'")).Should().Be(1);
    }

    [PostgresFact]
    public async Task Quitar_un_rol_ya_inactivo_tambien_purga_sus_overrides()
    {
        await using var cn = await SeedAsync();
        await ExecAsync(cn, "UPDATE security.roles SET is_active = false WHERE code = 'it46_old'");

        await RemoveHandler().HandleAsync(Target, Tenant, _roleOld, Actor, Ct);

        (await ActiveAsync(cn, "tramites")).Should().Be(0);
        (await PurgeAuditAsync(cn)).Should().Be(3);
    }

    [PostgresFact]
    public async Task Si_la_baja_del_rol_falla_no_se_purga()
    {
        await using var cn = await SeedAsync();
        var handler = RemoveHandler(inner => new FailingSoftDeleteRepository(inner));

        await FluentActions.Awaiting(() => handler.HandleAsync(Target, Tenant, _roleOld, Actor, Ct)).Should().ThrowAsync<InvalidOperationException>();

        (await ActiveAsync(cn, "tramites")).Should().Be(3);
        (await PurgeAuditAsync(cn)).Should().Be(0);
        (await ActiveAssignmentsAsync(cn, _roleOld)).Should().Be(1);
    }

    /// <summary>Falla al cerrar la asignacion del rol: la baja y la purga no deben quedar a medias.</summary>
    private sealed class FailingSoftDeleteRepository(IUserRoleAssignmentRepository inner) : IUserRoleAssignmentRepository
    {
        public Task<bool> UserBelongsToTenantAsync(Guid userId, Guid tenantId, CancellationToken ct) => inner.UserBelongsToTenantAsync(userId, tenantId, ct);

        public Task<Guid?> GetUserTenantAsync(Guid userId, CancellationToken ct) => inner.GetUserTenantAsync(userId, ct);

        public Task<RoleForAssignmentSnapshot?> GetActiveRoleAsync(Guid roleId, CancellationToken ct) => inner.GetActiveRoleAsync(roleId, ct);

        public Task<string?> GetRoleProductCodeAsync(Guid roleId, CancellationToken ct) => inner.GetRoleProductCodeAsync(roleId, ct);

        public Task<string> GetTenantTargetEntityTypeAsync(Guid tenantId, CancellationToken ct) => inner.GetTenantTargetEntityTypeAsync(tenantId, ct);

        public Task<UserRoleAssignmentSnapshot?> GetActiveAssignmentAsync(Guid userId, Guid tenantId, Guid roleId, CancellationToken ct) =>
            inner.GetActiveAssignmentAsync(userId, tenantId, roleId, ct);

        public Task<IReadOnlyList<UserRoleAssignmentSnapshot>> GetActiveAssignmentsAsync(Guid userId, Guid tenantId, CancellationToken ct) =>
            inner.GetActiveAssignmentsAsync(userId, tenantId, ct);

        public Task SoftDeleteAssignmentAsync(Guid assignmentId, Guid deletedBy, CancellationToken ct) =>
            throw new InvalidOperationException("fallo simulado al cerrar la asignacion");

        public Task<Guid> CreateAssignmentAsync(AssignRoleData data, CancellationToken ct) => inner.CreateAssignmentAsync(data, ct);
    }

    /// <summary>Falla al crear la asignacion nueva, ya con la anterior cerrada: la transaccion debe deshacerlo todo.</summary>
    private sealed class FailingCreateRepository(IUserRoleAssignmentRepository inner) : IUserRoleAssignmentRepository
    {
        public Task<bool> UserBelongsToTenantAsync(Guid userId, Guid tenantId, CancellationToken ct) => inner.UserBelongsToTenantAsync(userId, tenantId, ct);

        public Task<Guid?> GetUserTenantAsync(Guid userId, CancellationToken ct) => inner.GetUserTenantAsync(userId, ct);

        public Task<RoleForAssignmentSnapshot?> GetActiveRoleAsync(Guid roleId, CancellationToken ct) => inner.GetActiveRoleAsync(roleId, ct);

        public Task<string?> GetRoleProductCodeAsync(Guid roleId, CancellationToken ct) => inner.GetRoleProductCodeAsync(roleId, ct);

        public Task<string> GetTenantTargetEntityTypeAsync(Guid tenantId, CancellationToken ct) => inner.GetTenantTargetEntityTypeAsync(tenantId, ct);

        public Task<UserRoleAssignmentSnapshot?> GetActiveAssignmentAsync(Guid userId, Guid tenantId, Guid roleId, CancellationToken ct) =>
            inner.GetActiveAssignmentAsync(userId, tenantId, roleId, ct);

        public Task<IReadOnlyList<UserRoleAssignmentSnapshot>> GetActiveAssignmentsAsync(Guid userId, Guid tenantId, CancellationToken ct) =>
            inner.GetActiveAssignmentsAsync(userId, tenantId, ct);

        public Task SoftDeleteAssignmentAsync(Guid assignmentId, Guid deletedBy, CancellationToken ct) =>
            inner.SoftDeleteAssignmentAsync(assignmentId, deletedBy, ct);

        public Task<Guid> CreateAssignmentAsync(AssignRoleData data, CancellationToken ct) =>
            throw new InvalidOperationException("fallo simulado al crear la asignacion");
    }
}
