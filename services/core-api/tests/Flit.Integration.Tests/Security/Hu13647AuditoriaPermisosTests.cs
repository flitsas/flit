using Flit.Infrastructure.Persistence.Repositories;
using Flit.Integration.Tests.Postgres;
using Flit.Modules.Security.Application.PermissionOverrides;
using Flit.Modules.Security.Domain.PermissionOverrides;
using FluentAssertions;
using Npgsql;
using Xunit;

namespace Flit.Integration.Tests.Security;

/// <summary>
/// HU #13647 (Feature #13101 F-B, Epica #12753) contra PostgreSQL real con 2 tenants: Super Admin ve todo y filtra por
/// usuario/tenant/actor (AC1), el administrador solo su ambito y otro tenant = 403 (AC2), contenido con nombres legibles
/// (AC3) y rechazos + PII_REVEAL_CONFIRMED visibles (AC4). PII_REVEAL_CONFIRMED se siembra directo en la tabla: el
/// endpoint que lo escribe llega con la UI de F-D1; aqui solo se valida que la consulta lo devuelve.
/// </summary>
public sealed class Hu13647AuditoriaPermisosTests(PostgresDatabaseFixture fixture) : PostgresTestBase(fixture)
{
    private static readonly Guid TenantA = new("aaaaaaaa-1364-4700-8000-00000000000a");
    private static readonly Guid TenantB = new("bbbbbbbb-1364-4700-8000-00000000000b");
    private static readonly Guid ActorA = new("a1000000-1364-4700-8000-000000000001");
    private static readonly Guid UserA = new("a2000000-1364-4700-8000-000000000002");
    private static readonly Guid ActorB = new("b1000000-1364-4700-8000-000000000001");
    private static readonly Guid UserB = new("b2000000-1364-4700-8000-000000000002");

    private readonly List<Flit.Infrastructure.Persistence.FlitDbContext> _contexts = [];

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

    private PermissionAuditReader Reader()
    {
        var ctx = NewContext();
        _contexts.Add(ctx);
        return new PermissionAuditReader(ctx);
    }

    private QueryPermissionAuditHandler Handler() => new(Reader());

    private static string Quote(string? v) => v is null ? "NULL" : $"'{v}'";

    private static string Row(Guid tenant, Guid actor, Guid target, string action, string outcome, string? reason, string? before, string? after, int minutesAgo) =>
        $"""
        INSERT INTO security.user_permission_audit
          (tenant_id, actor_user_id, target_user_id, product_code, module_code, permission_slug, action, outcome, reason_code, value_before, value_after, created_at)
        VALUES ('{tenant}','{actor}','{target}','tramites','it47_m','it47.reportes.read','{action}','{outcome}',
                {Quote(reason)}, {Quote(before)}, {Quote(after)}, now() - interval '{minutesAgo} minutes');
        """;

    private async Task SeedAsync()
    {
        await using var cn = await Fixture.OpenConnectionAsync();
        await ExecAsync(cn, $"""
            INSERT INTO platform.products (code, name, icon) VALUES ('tramites','Tramites','file') ON CONFLICT (code) DO NOTHING;
            INSERT INTO identity.tenants (id, code, legal_name, tax_id, tenant_type, is_active, is_group_parent, created_at) VALUES
              ('{TenantA}','IT-47-A','Compania A','9136470000001','CONCESION',true,true,now()),
              ('{TenantB}','IT-47-B','Compania B','9136470000002','CONCESIONARIO',true,false,now());
            INSERT INTO identity.users (id, email, display_name, status, home_tenant_id, created_at) VALUES
              ('{ActorA}','actor13647a@it.local','Admin Uno','active','{TenantA}',now()),
              ('{UserA}','ua13647@it.local','Usuario A','active','{TenantA}',now()),
              ('{ActorB}','actor13647b@it.local','Admin Dos','active','{TenantB}',now()),
              ('{UserB}','ub13647@it.local','Usuario B','active','{TenantB}',now());
            """);
        await ExecAsync(cn, Row(TenantA, ActorA, UserA, "GRANT", "OK", null, null, "allow", 30));
        await ExecAsync(cn, Row(TenantA, ActorA, UserA, "GRANT", "REJECTED", "OUT_OF_CAP", null, "allow", 20));
        await ExecAsync(cn, Row(TenantA, ActorA, UserA, "PII_REVEAL_CONFIRMED", "OK", null, null, null, 10));
        await ExecAsync(cn, Row(TenantB, ActorB, UserB, "GRANT", "OK", null, "deny", "allow", 5));
    }

    [Fact]
    public async Task AC1_SuperAdmin_ve_todos_los_tenants_y_filtra_por_usuario_tenant_y_actor()
    {
        await SeedAsync();
        var handler = Handler();

        var all = await handler.HandleAsync(Guid.NewGuid(), true, null, null, null, null, null, null, 100, Ct);
        all.Items.Select(i => i.TenantId).Should().Contain([TenantA, TenantB]);
        all.Items.Where(i => i.TenantId == TenantA || i.TenantId == TenantB).Should().HaveCount(4);

        var byUser = await handler.HandleAsync(Guid.NewGuid(), true, null, UserB, null, null, null, null, 100, Ct);
        byUser.Items.Should().ContainSingle().Which.TargetUserId.Should().Be(UserB);

        var byTenant = await handler.HandleAsync(Guid.NewGuid(), true, TenantA, null, null, null, null, null, 100, Ct);
        byTenant.Items.Should().HaveCount(3).And.OnlyContain(i => i.TenantId == TenantA);

        var byActor = await handler.HandleAsync(Guid.NewGuid(), true, null, null, ActorB, null, null, null, 100, Ct);
        byActor.Items.Should().ContainSingle().Which.ActorUserId.Should().Be(ActorB);
    }

    [Fact]
    public async Task AC2_Administrador_ve_solo_su_ambito_y_otro_tenant_da_403()
    {
        await SeedAsync();
        var handler = Handler();

        var own = await handler.HandleAsync(TenantA, false, null, null, null, null, null, null, 100, Ct);
        own.Items.Should().HaveCount(3).And.OnlyContain(i => i.TenantId == TenantA);

        var explicitOwn = await handler.HandleAsync(TenantA, false, TenantA, UserB, null, null, null, null, 100, Ct);
        explicitOwn.Items.Should().BeEmpty("un usuario de otro tenant no se cuela filtrando por userId");

        var act = () => handler.HandleAsync(TenantA, false, TenantB, null, null, null, null, null, 100, Ct);
        await act.Should().ThrowAsync<PermissionAuditScopeException>();
    }

    [Fact]
    public async Task AC3_Cada_registro_trae_nombres_valores_y_fecha_ordenados_por_fecha_desc()
    {
        await SeedAsync();
        var page = await Handler().HandleAsync(TenantB, false, null, null, null, null, null, null, 100, Ct);

        var r = page.Items.Should().ContainSingle().Subject;
        r.ActorName.Should().Be("Admin Dos");
        r.ActorEmail.Should().Be("actor13647b@it.local");
        r.TargetName.Should().Be("Usuario B");
        r.TargetEmail.Should().Be("ub13647@it.local");
        r.TenantName.Should().Be("Compania B");
        r.PermissionSlug.Should().Be("it47.reportes.read");
        (r.ValueBefore, r.ValueAfter, r.Outcome).Should().Be(("deny", "allow", "OK"));
        r.CreatedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(10));

        var a = await Handler().HandleAsync(TenantA, false, null, null, null, null, null, null, 100, Ct);
        a.Items.Select(i => i.CreatedAt).Should().BeInDescendingOrder();
    }

    [Fact]
    public async Task AC3_El_permiso_y_el_modulo_llegan_con_su_nombre_legible_no_con_el_codigo()
    {
        await SeedAsync();
        await using (var cn = await Fixture.OpenConnectionAsync())
        {
            await ExecAsync(cn, """
                INSERT INTO security.modules (code, name, sort_order, is_active, product_code) VALUES ('it47_m','Reportes IT 47',1,true,'tramites');
                INSERT INTO security.permissions (module_id, slug, name, http_method, route_pattern, is_active, enforced)
                  SELECT id, 'it47.reportes.read', 'Ver reportes', 'GET', '/api/v1/it47/r', true, 'true'
                  FROM security.modules WHERE code = 'it47_m';
                """);
        }

        var r = (await Handler().HandleAsync(TenantB, false, null, null, null, null, null, null, 100, Ct)).Items.Should().ContainSingle().Subject;

        (r.PermissionName, r.ModuleName).Should().Be(("Ver reportes", "Reportes IT 47"));
    }

    [Fact]
    public async Task AC4_Rechazos_y_confirmacion_PII_aparecen_en_la_consulta()
    {
        await SeedAsync();
        var page = await Handler().HandleAsync(TenantA, false, null, null, null, null, null, null, 100, Ct);

        page.Items.Should().Contain(i => i.Outcome == "REJECTED" && i.ReasonCode == "OUT_OF_CAP");
        page.Items.Should().Contain(i => i.Action == PermissionAuditActions.PiiRevealConfirmed);
    }

    [Fact]
    public async Task Paginacion_y_rango_de_fechas()
    {
        await SeedAsync();
        var handler = Handler();

        var p1 = await handler.HandleAsync(TenantA, false, null, null, null, null, null, 1, 2, Ct);
        var p2 = await handler.HandleAsync(TenantA, false, null, null, null, null, null, 2, 2, Ct);
        (p1.Total, p1.Items.Count, p2.Items.Count).Should().Be((3, 2, 1));

        var recent = await handler.HandleAsync(
            TenantA, false, null, null, null, DateTimeOffset.UtcNow.AddMinutes(-15), null, null, 100, Ct);
        recent.Items.Should().ContainSingle().Which.Action.Should().Be(PermissionAuditActions.PiiRevealConfirmed);
    }

    [Fact]
    public async Task FD1_8_Overrides_activos_traen_quien_otorgo_y_cuando()
    {
        await SeedAsync();
        await using (var cn = await Fixture.OpenConnectionAsync())
        {
            await ExecAsync(cn, $"""
                INSERT INTO security.modules (code, name, sort_order, is_active, product_code) VALUES ('it47_m','IT 47',1,true,'tramites');
                INSERT INTO security.permissions (module_id, slug, name, http_method, route_pattern, is_active, enforced)
                  SELECT id, 'it47.reportes.read', 'it47.reportes.read', 'GET', '/api/v1/it47/r', true, 'true'
                    FROM security.modules WHERE code = 'it47_m';
                INSERT INTO security.user_permission_overrides (tenant_id, user_id, product_code, permission_id, effect, granted_by)
                  SELECT '{TenantA}','{UserA}','tramites', id, 'allow', '{ActorA}' FROM security.permissions WHERE slug = 'it47.reportes.read';
                """);
        }

        var info = await Reader().GetGrantInfoAsync(TenantA, UserA, Ct);

        var g = info.Should().ContainSingle().Subject;
        g.GrantedByName.Should().Be("Admin Uno");
        g.GrantedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(10));
    }
}
