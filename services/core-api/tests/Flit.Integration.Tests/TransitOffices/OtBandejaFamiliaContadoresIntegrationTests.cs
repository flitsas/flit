using Flit.Admin.Application.OtClientProcedures.GetOtBandejaCounters;
using Flit.Admin.Domain.OtClientProcedures;
using Flit.Infrastructure.Persistence.Repositories;
using Flit.Integration.Tests.Postgres;
using Flit.Integration.Tests.Tenancy;
using Flit.Tramites.Domain.Tramites.Estados;
using FluentAssertions;
using Xunit;

namespace Flit.Integration.Tests.TransitOffices;

/// <summary>
/// Epic #12686 (HU #12803) — contadores de la bandeja del OT bajo la familia y los filtros de la
/// tabla, contra PostgreSQL REAL: la comparación por familia del tipo y la búsqueda libre se
/// traducen al motor, que es lo que InMemory no puede demostrar.
/// </summary>
public sealed class OtBandejaFamiliaContadoresIntegrationTests(PostgresDatabaseFixture fixture) : PostgresTestBase(fixture)
{
    [PostgresFact]
    public async Task Contadores_por_familia_cuentan_solo_esa_familia_y_coinciden_con_la_tabla()
    {
        var tipo = await HierarchyScenario.SeedAsync(Fixture);
        var familia = tipo.Family;
        var otra = familia == "TRASPASO" ? "MATRICULAS" : "TRASPASO";

        var todos = await Contar(null);
        var deLaFamilia = await Contar(new OtClientProcedureFilter { Familia = familia.ToLowerInvariant() });
        var deOtra = await Contar(new OtClientProcedureFilter { Familia = otra });

        todos.PorDecidir.Should().BePositive();
        deLaFamilia.PorDecidir.Should().Be(todos.PorDecidir, "todo lo sembrado es de la misma familia");
        deOtra.PorDecidir.Should().Be(0);

        await using var ctx = NewContext();
        var tabla = await new OtClientProcedureRepository(ctx, new NullTramiteTransitionPublisher()).ListAsync(
            HierarchyScenario.O,
            new OtClientProcedureFilter { Familia = familia, Status = TramiteEstado.Entregado, Page = 1, PageSize = 100 });
        tabla.TotalCount.Should().Be(deLaFamilia.PorDecidir);
    }

    [PostgresFact]
    public async Task Contadores_siguen_la_busqueda_libre_de_la_bandeja()
    {
        await HierarchyScenario.SeedAsync(Fixture);
        // La razón social de la compañía cliente lleva su código: solo el entregado de C1.
        var filtrados = await Contar(new OtClientProcedureFilter { Busqueda = "IT-C1" });

        filtrados.PorDecidir.Should().Be(1);
    }

    private async Task<GetOtBandejaCountersResult> Contar(OtClientProcedureFilter? filtro)
    {
        await using var ctx = NewContext();
        var repo = new OtClientProcedureRepository(ctx, new NullTramiteTransitionPublisher());
        return await new GetOtBandejaCountersHandler(repo).HandleAsync(
            new GetOtBandejaCountersQuery { OtTenantId = HierarchyScenario.O, Filtro = filtro });
    }
}
