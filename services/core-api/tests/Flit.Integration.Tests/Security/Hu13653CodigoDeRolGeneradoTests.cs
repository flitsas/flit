using Flit.Infrastructure.Persistence.Repositories;
using Flit.Integration.Tests.Postgres;
using Flit.Modules.Security.Application.Roles;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace Flit.Integration.Tests.Security;

/// <summary>
/// HU #13653 / #13666 (Épica #12753) — el código de un rol nuevo lo genera el servidor a partir del nombre, contra
/// PostgreSQL real (base efímera del arnés, nunca <c>flit_v2_local</c>): índices únicos <c>uq_roles_tenant_code</c> y
/// <c>uq_roles_code_target_entity_type</c> y el disparador de codes reservados del DDL 134.
/// </summary>
public sealed class Hu13653CodigoDeRolGeneradoTests(PostgresDatabaseFixture fixture) : PostgresTestBase(fixture)
{
    private static readonly Guid Tenant = new("aaaaaaaa-1365-4000-8000-00000000000a");
    private static readonly Guid OtroTenant = new("bbbbbbbb-1365-4000-8000-00000000000b");

    private async Task SeedAsync()
    {
        await using var cn = await Fixture.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand($"""
            INSERT INTO platform.products (code, name, icon) VALUES
              ('plataforma','Plataforma','layout'),('tramites','Tramites','file'),
              ('comparendos','Comparendos','file'),('diagnostico','Diagnostico','file')
            ON CONFLICT (code) DO NOTHING;
            INSERT INTO identity.tenants (id, code, legal_name, tax_id, tenant_type, is_active, is_group_parent, created_at)
              VALUES ('{Tenant}','IT-13653-A','A','9136530000001','CONCESIONARIO',true,false,now()),
                     ('{OtroTenant}','IT-13653-B','B','9136530000002','CONCESIONARIO',true,false,now());
            INSERT INTO platform.tenant_products (tenant_id, product_code, enabled)
              VALUES ('{Tenant}','tramites',true), ('{OtroTenant}','tramites',true)
            ON CONFLICT (tenant_id, product_code) DO UPDATE SET enabled = true;
            INSERT INTO security.roles (code, name, target_entity_type, is_system)
              VALUES ('AdminCompany','Admin','COMPANY',true), ('ot_admin','OT','TRANSIT_OFFICE',true);
            """, cn);
        await cmd.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    private async Task<string> CodeOfAsync(Guid id)
    {
        await using var ctx = NewContext();
        return (await ctx.Roles.AsNoTracking().SingleAsync(r => r.Id == id, TestContext.Current.CancellationToken)).Code;
    }

    private async Task<Guid> CrearRolPropioAsync(string name, string? code = null, Guid? tenant = null)
    {
        await using var ctx = NewContext();
        return await new CreateTenantRoleHandler(new RoleRepository(ctx)).HandleAsync(
            new CreateTenantRoleCommand(tenant ?? Tenant, "COMPANY", code, name, null, "tramites", [], []),
            TestContext.Current.CancellationToken);
    }

    private async Task<Guid> CrearRolGlobalAsync(string name, string? code = null, string target = "COMPANY")
    {
        await using var ctx = NewContext();
        return await new CreateRoleHandler(new RoleRepository(ctx)).HandleAsync(
            new CreateRoleCommand(target, code, name, null), TestContext.Current.CancellationToken);
    }

    [PostgresFact]
    public async Task RolPropio_DosConElMismoNombre_ObtienenCodigosDistintos()
    {
        await SeedAsync();

        var primero = await CrearRolPropioAsync("Coordinación Señal");
        var segundo = await CrearRolPropioAsync("Coordinación Señal");

        (await CodeOfAsync(primero)).Should().Be("coordinacion_senal");
        (await CodeOfAsync(segundo)).Should().Be("coordinacion_senal_2");
    }

    [PostgresFact]
    public async Task RolPropio_ConNombreDeUnRolGlobal_NoChocaConElGlobal()
    {
        await SeedAsync();

        var id = await CrearRolPropioAsync("AdminCompany");

        (await CodeOfAsync(id)).Should().Be("admincompany_2", "el disparador compara lower(code) contra los globales");
    }

    [PostgresFact]
    public async Task RolPropio_OtroTenantPuedeRepetirElCodigoGenerado()
    {
        await SeedAsync();

        var a = await CrearRolPropioAsync("Contador");
        var b = await CrearRolPropioAsync("Contador", tenant: OtroTenant);

        (await CodeOfAsync(a)).Should().Be("contador");
        (await CodeOfAsync(b)).Should().Be("contador");
    }

    [PostgresFact]
    public async Task RolPropio_ConCodigoExplicito_SeRespeta()
    {
        await SeedAsync();

        var id = await CrearRolPropioAsync("Contador", code: "Cont.Externo-1");

        (await CodeOfAsync(id)).Should().Be("Cont.Externo-1");
    }

    [PostgresFact]
    public async Task RolPropio_CreacionesSimultaneasDelMismoNombre_NoRepitenCodigo()
    {
        await SeedAsync();

        var ids = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => CrearRolPropioAsync("Analista")));

        var codes = await Task.WhenAll(ids.Select(CodeOfAsync));
        codes.Should().OnlyHaveUniqueItems().And.OnlyContain(c => c == "analista" || c.StartsWith("analista_"));
    }

    [PostgresFact]
    public async Task RolGlobal_DosConElMismoNombre_ObtienenCodigosDistintos_YElCodigoExplicitoSigueFuncionando()
    {
        await SeedAsync();

        var primero = await CrearRolGlobalAsync("Auditor de Trámites");
        var segundo = await CrearRolGlobalAsync("Auditor de Trámites");
        var explicito = await CrearRolGlobalAsync("Otro", code: "AUDITOR_X");

        (await CodeOfAsync(primero)).Should().Be("auditor_de_tramites");
        (await CodeOfAsync(segundo)).Should().Be("auditor_de_tramites_2");
        (await CodeOfAsync(explicito)).Should().Be("AUDITOR_X");
    }

    [PostgresFact]
    public async Task RolGlobal_NoTomaElCodigoDeUnRolDeCompania()
    {
        await SeedAsync();
        await CrearRolPropioAsync("Supervisor");

        var global = await CrearRolGlobalAsync("Supervisor");

        (await CodeOfAsync(global)).Should().Be("supervisor_2");
    }
}
