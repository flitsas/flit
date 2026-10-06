using Flit.Infrastructure.Persistence.Repositories;
using Flit.Integration.Tests.Postgres;
using Flit.Integration.Tests.Tenancy;
using Flit.Queries.Domain.Tenancy;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Tramites.Estados;
using FluentAssertions;
using Xunit;

namespace Flit.Integration.Tests.Network;

/// <summary>
/// Epic #12686 (HU #12805) — la búsqueda rápida también en la vista consolidada de la red, contra
/// PostgreSQL real sobre <see cref="HierarchyScenario"/> (P cabeza; C1 y C2 hijos; X ajeno). El
/// atajo se evalúa sobre el alcance de la red y nunca trae trámites de fuera.
/// </summary>
public sealed class NetworkBusquedaRapidaTests(PostgresDatabaseFixture fixture) : PostgresTestBase(fixture)
{
    private static TenantScope GroupP() =>
        TenantScope.Group(HierarchyScenario.P, [HierarchyScenario.C1, HierarchyScenario.C2], GroupKind.Concesion);

    [PostgresFact]
    public async Task SinFirmas_en_la_red_solo_trae_borradores_del_alcance()
    {
        await HierarchyScenario.SeedAsync(Fixture);
        await using var ctx = NewContext();
        var repo = new ProcedureInstanceRepository(ctx);

        var (items, total, error) = await new NetworkListProcedureInstancesHandler(repo).HandleAsync(
            GroupP(), childTenantId: null, new ProcedureInstanceListRequest { BusquedaRapida = BusquedaRapida.SinFirmas });
        var (conteos, errorConteos) = await new NetworkCountProcedureInstancesByStatusHandler(repo).HandleAsync(
            GroupP(), childTenantId: null, new ProcedureInstanceListRequest { BusquedaRapida = BusquedaRapida.SinFirmas });

        error.Should().BeNull();
        errorConteos.Should().BeNull();
        items.Should().OnlyContain(i => i.Estado == TramiteEstado.Borrador);
        items.Select(i => i.TenantId).Should().OnlyContain(t =>
            t == HierarchyScenario.P || t == HierarchyScenario.C1 || t == HierarchyScenario.C2);
        items.Select(i => i.Id).Should().NotContain(HierarchyScenario.DraftProcedureOf(HierarchyScenario.X));
        conteos![TramiteEstado.Borrador].Should().Be(total);
        conteos[TramiteEstado.Entregado].Should().Be(0);
    }

    [PostgresFact]
    public async Task MisTramites_en_la_red_trae_los_del_usuario_dentro_del_alcance()
    {
        await HierarchyScenario.SeedAsync(Fixture);
        await using var ctx = NewContext();

        var (items, total, error) = await new NetworkListProcedureInstancesHandler(new ProcedureInstanceRepository(ctx))
            .HandleAsync(GroupP(), childTenantId: null, new ProcedureInstanceListRequest
            {
                BusquedaRapida = BusquedaRapida.MisTramites,
                UsuarioActualId = HierarchyScenario.UserOf(HierarchyScenario.C1),
            });

        error.Should().BeNull();
        total.Should().Be(2);
        items.Select(i => i.Id).Should().BeEquivalentTo(HierarchyScenario.ProceduresOf(HierarchyScenario.C1));
    }
}
