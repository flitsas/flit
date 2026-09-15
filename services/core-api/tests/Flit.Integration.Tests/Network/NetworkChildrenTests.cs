using Flit.Admin.Domain.Companies;
using Flit.Infrastructure.Persistence.Repositories;
using Flit.Integration.Tests.Postgres;
using Flit.Integration.Tests.Tenancy;
using FluentAssertions;
using Xunit;

namespace Flit.Integration.Tests.Network;

/// <summary>
/// HU #12555 (Feature #12257, épica #12235) — clientes hijos vigentes de la cabeza de grupo, contra
/// PostgreSQL real sobre <see cref="HierarchyScenario"/> (P cabeza; C1/C2 hijos; X ajeno; S aislado).
/// El endpoint <c>GET /api/v1/tramites/network/children</c> (<c>Flit.Api.Endpoints.Tramites.NetworkChildrenEndpoints</c>,
/// fuera del alcance de este proyecto por no referenciar <c>Flit.Api</c>) compone
/// <see cref="ICompanyHierarchyRepository.ListChildrenAsync"/> con una proyección
/// <c>id</c>/<c>nombre</c> ordenada por nombre — el 403 de <c>Single</c>/SuperAdmin (AC2) y la
/// indiferencia a cabeceras/query manipulados (AC4) los cubre <c>NetworkChildrenEndpointsTests</c>
/// en <c>Flit.Admin.Tests</c> (host HTTP con <see cref="GroupHeadReadFilter"/> real). Aquí se prueba
/// SOLO el dato: exactamente los hijos vigentes de P, sin X ni el propio P (AC1/AC3), y que un hijo
/// desvinculado deja de aparecer en la siguiente consulta sin caché (AC3).
/// <para>
/// Uso de ejemplo: <c>await new CompanyHierarchyRepository(ctx).ListChildrenAsync(HierarchyScenario.P, ct)</c>
/// devuelve <c>[C1, C2]</c> (orden de inserción del repositorio; el endpoint reordena por nombre).
/// </para>
/// </summary>
public sealed class NetworkChildrenTests(PostgresDatabaseFixture fixture) : PostgresTestBase(fixture)
{
    // ── AC1 — cabeza de grupo obtiene exactamente sus hijos ───────────────────────────────────

    [PostgresFact]
    public async Task AC1_Cabeza_obtiene_exactamente_sus_dos_hijos_con_id_y_razon_social()
    {
        await HierarchyScenario.SeedAsync(Fixture);
        await using var ctx = NewContext();
        var repo = new CompanyHierarchyRepository(ctx);

        var children = await repo.ListChildrenAsync(HierarchyScenario.P, TestContext.Current.CancellationToken);

        children.Select(c => c.Id).Should().BeEquivalentTo([HierarchyScenario.C1, HierarchyScenario.C2]);
        children.Should().OnlyContain(c => c.EstadoActivo);
        children.Single(c => c.Id == HierarchyScenario.C1).RazonSocial.Should().Contain("IT-C1");
        children.Single(c => c.Id == HierarchyScenario.C2).RazonSocial.Should().Contain("IT-C2");
    }

    // ── AC3 — red P con C1, C2 y X ajeno: X nunca aparece, ni el propio P ─────────────────────

    [PostgresFact]
    public async Task AC3_X_ajeno_y_la_propia_cabeza_P_nunca_aparecen_en_los_hijos_de_P()
    {
        await HierarchyScenario.SeedAsync(Fixture);
        await using var ctx = NewContext();
        var repo = new CompanyHierarchyRepository(ctx);

        var children = await repo.ListChildrenAsync(HierarchyScenario.P, TestContext.Current.CancellationToken);

        children.Should().NotContain(c => c.Id == HierarchyScenario.X);
        children.Should().NotContain(c => c.Id == HierarchyScenario.P);
        children.Should().NotContain(c => c.Id == HierarchyScenario.S);
    }

    // ── AC3 — desvincular un hijo deja de listarlo en la petición inmediatamente siguiente ────

    [PostgresFact]
    public async Task AC3_Hijo_desvinculado_deja_de_aparecer_en_la_peticion_siguiente()
    {
        await HierarchyScenario.SeedAsync(Fixture);
        await using var writeCtx = NewContext();
        var writeRepo = new CompanyHierarchyRepository(writeCtx);

        var antes = await writeRepo.ListChildrenAsync(HierarchyScenario.P, TestContext.Current.CancellationToken);
        antes.Select(c => c.Id).Should().BeEquivalentTo([HierarchyScenario.C1, HierarchyScenario.C2]);

        // Desvincular C1 (SetParentAsync(childTenantId, parentTenantId: null, ...)) — mismo mecanismo
        // que usa el panel admin (HU #12355) para soltar un hijo de la cabeza.
        var updated = await writeRepo.SetParentAsync(
            HierarchyScenario.C1, parentTenantId: null, changedBy: null, TestContext.Current.CancellationToken);
        updated.Should().NotBeNull();

        // Nueva petición (nuevo contexto, sin caché de EF) ⇒ ya no ve a C1.
        await using var readCtx = NewContext();
        var readRepo = new CompanyHierarchyRepository(readCtx);
        var despues = await readRepo.ListChildrenAsync(HierarchyScenario.P, TestContext.Current.CancellationToken);

        despues.Select(c => c.Id).Should().BeEquivalentTo([HierarchyScenario.C2]);
        despues.Should().NotContain(c => c.Id == HierarchyScenario.C1);
    }

    // ── AC1 — red sin hijos ⇒ lista vacía (S es Single, sin jerarquía) ────────────────────────

    [PostgresFact]
    public async Task Cabeza_sin_hijos_devuelve_lista_vacia()
    {
        await HierarchyScenario.SeedAsync(Fixture);
        await using var ctx = NewContext();
        var repo = new CompanyHierarchyRepository(ctx);

        var children = await repo.ListChildrenAsync(HierarchyScenario.S, TestContext.Current.CancellationToken);

        children.Should().BeEmpty();
    }
}
