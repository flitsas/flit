using Flit.Infrastructure.Persistence.Repositories;
using Flit.Integration.Tests.MarcaBlanca;
using Flit.Integration.Tests.Postgres;
using Flit.Modules.Security.Application.PermissionOverrides;
using Flit.Modules.Security.Application.PermissionOverrides.GrantModule;
using Flit.Modules.Security.Domain.PermissionOverrides;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace Flit.Integration.Tests.Security;

/// <summary>
/// Hallazgo review Épica #12753 (HU #13644) contra PostgreSQL real y el host real: el catálogo otorgable y los overrides
/// respetan <c>Security:ModulePermissionEnforcement:Enabled</c>. Con el flag apagado <c>reportes.read</c> (exigido por
/// <c>RequireAnyModulePermission</c> en la API) no se ofrece ni se acepta; encendido, sí.
/// </summary>
public sealed class Hu13644PermisosIndividualesFlagEnforcementTests(PostgresDatabaseFixture fixture) : PostgresTestBase(fixture)
{
    private static readonly Guid Tenant = new("aaaaaaaa-1364-4401-8000-00000000000a");
    private static readonly Guid SuperAdmin = new("a4000000-1364-4401-8000-000000000004");
    private static readonly Guid Target = new("a3000000-1364-4401-8000-000000000003");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<Guid> SeedAsync()
    {
        await using var cn = await Fixture.OpenConnectionAsync();
        await using (var cmd = new NpgsqlCommand($"""
            INSERT INTO platform.products (code, name, icon) VALUES ('tramites','Tramites','file') ON CONFLICT (code) DO NOTHING;
            INSERT INTO identity.tenants (id, code, legal_name, tax_id, tenant_type, is_active, is_group_parent, created_at)
              VALUES ('{Tenant}','IT-44-F','F','9136440100001','CONCESIONARIO',true,false,now());
            INSERT INTO identity.users (id, email, display_name, status, created_at) VALUES
              ('{SuperAdmin}','sa1364f@it.local','Sa','active',now()), ('{Target}','t1364f@it.local','T','active',now());
            INSERT INTO security.modules (code, name, sort_order, is_active, product_code) VALUES ('it44f_m','IT 44 F',1,true,'tramites');
            INSERT INTO security.permissions (module_id, slug, name, http_method, route_pattern, is_active, enforced)
              SELECT id, v.slug, v.slug, 'GET', '/api/v1/it44f/' || v.slug, true, 'true'
                FROM security.modules, (VALUES ('reportes.read'), ('it44f.siempre')) AS v(slug) WHERE code = 'it44f_m';
            """, cn))
        {
            await cmd.ExecuteNonQueryAsync(Ct);
        }

        await using var q = new NpgsqlCommand("SELECT id FROM security.permissions WHERE slug = 'reportes.read'", cn);
        return (Guid)(await q.ExecuteScalarAsync(Ct))!;
    }

    private async Task<(IReadOnlyList<string> Grantable, string? SetReason)> RunAsync(bool moduleFlag, Guid reportesId)
    {
        await using var factory = new MarcaBlancaApiFactory(Fixture);
        await using var host = factory.WithWebHostBuilder(b => b.ConfigureAppConfiguration((_, c) =>
            c.AddInMemoryCollection(new Dictionary<string, string?> { ["Security:ModulePermissionEnforcement:Enabled"] = moduleFlag ? "true" : "false" })));
        var state = host.Services.GetRequiredService<IPermissionEnforcementState>();

        await using var ctx = NewContext();
        var repo = new UserPermissionOverrideRepository(ctx);
        var handler = new ManageUserPermissionOverridesHandler(
            repo, new PermissionCapCalculator(repo), new GrantModuleOverridesHandler(repo), [], state);
        var actor = new OverrideActor(SuperAdmin, true);

        var grantable = (await handler.ListGrantableAsync(Tenant, actor, Ct)).Select(p => p.Slug).ToList();
        string? reason = null;
        try
        {
            await handler.SetAsync(Tenant, actor, Target, reportesId, "deny", Ct);
        }
        catch (PermissionOverrideRejectedException ex)
        {
            reason = ex.ReasonCode;
        }

        return (grantable, reason);
    }

    [PostgresFact]
    public async Task Con_el_flag_de_modulos_apagado_reportes_read_no_es_otorgable_ni_se_acepta()
    {
        var reportes = await SeedAsync();

        var (grantable, reason) = await RunAsync(moduleFlag: false, reportes);

        grantable.Should().NotContain("reportes.read").And.Contain("it44f.siempre");
        reason.Should().Be("NOT_ENFORCED");
    }

    [PostgresFact]
    public async Task Con_el_flag_de_modulos_encendido_reportes_read_es_otorgable_y_se_acepta()
    {
        var reportes = await SeedAsync();

        var (grantable, reason) = await RunAsync(moduleFlag: true, reportes);

        grantable.Should().Contain(["reportes.read", "it44f.siempre"]);
        reason.Should().BeNull();
    }
}
