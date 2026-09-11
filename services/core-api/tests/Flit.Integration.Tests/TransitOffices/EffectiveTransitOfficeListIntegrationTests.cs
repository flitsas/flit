using Flit.Admin.Application.Auditing;
using Flit.Infrastructure.OtRules;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Repositories;
using Flit.Integration.Tests.Postgres;
using Flit.Integration.Tests.Tenancy;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Flit.Integration.Tests.TransitOffices;

/// <summary>HU #12347 — lista efectiva de OT por tipo de cabeza (AC12, PostgreSQL real).</summary>
public sealed class EffectiveTransitOfficeListIntegrationTests(PostgresDatabaseFixture fixture) : PostgresTestBase(fixture)
{
    private static EffectiveTransitOfficeListResolver NewResolver(FlitDbContext ctx) =>
        new(
            new CompanyHierarchyRepository(ctx),
            new TransitGrantRepository(ctx, NullAuditContextAccessor.Instance),
            new TenantTransitOfficeBlockRepository(ctx, NullAuditContextAccessor.Instance),
            new DbTransitOfficeOperationalStatusReader(ctx),
            NullLogger<EffectiveTransitOfficeListResolver>.Instance);

    [PostgresFact]
    public async Task AC1_Cliente_sin_padre_usa_sus_propios_grants()
    {
        await HierarchyScenario.SeedAsync(Fixture);
        await using var ctx = NewContext();
        var resolver = NewResolver(ctx);

        var effective = await resolver.ListEffectiveOfficeIdsAsync(HierarchyScenario.S);

        effective.Should().Equal([HierarchyScenario.Ot2]);
    }

    [PostgresFact]
    public async Task AC2_Hijo_de_Concesion_hereda_grants_de_la_cabeza()
    {
        await HierarchyScenario.SeedAsync(Fixture);
        await using (var ctx = NewContext())
        {
            await TransitNetworkSeed.SetHeadGrantsAsync(ctx, HierarchyScenario.P, HierarchyScenario.Ot1);
        }

        await using var read = NewContext();
        var resolver = NewResolver(read);

        var head = await resolver.ListEffectiveOfficeIdsAsync(HierarchyScenario.P);
        var child = await resolver.ListEffectiveOfficeIdsAsync(HierarchyScenario.C1);

        head.Should().Equal([HierarchyScenario.Ot1]);
        child.Should().Equal([HierarchyScenario.Ot1]);
    }

    [PostgresFact]
    public async Task AC3_Grants_propios_del_hijo_no_amplian_la_lista()
    {
        await HierarchyScenario.SeedAsync(Fixture);
        await using (var ctx = NewContext())
        {
            await TransitNetworkSeed.SetHeadGrantsAsync(ctx, HierarchyScenario.P, HierarchyScenario.Ot2);
        }

        await using var read = NewContext();
        var resolver = NewResolver(read);

        // C1 tiene grant propio a Ot1 en la semilla, pero la cabeza solo tiene Ot2.
        var effective = await resolver.ListEffectiveOfficeIdsAsync(HierarchyScenario.C1);

        effective.Should().Equal([HierarchyScenario.Ot2]);
        effective.Should().NotContain(HierarchyScenario.Ot1);
    }

    [PostgresFact]
    public async Task AC6_Lista_vacia_de_Concesion_implica_lista_vacia_del_hijo()
    {
        await HierarchyScenario.SeedAsync(Fixture);
        await using (var ctx = NewContext())
        {
            await TransitNetworkSeed.SetHeadGrantsAsync(ctx, HierarchyScenario.P);
        }

        await using var read = NewContext();
        var resolver = NewResolver(read);

        (await resolver.ListEffectiveOfficeIdsAsync(HierarchyScenario.P)).Should().BeEmpty();
        (await resolver.ListEffectiveOfficeIdsAsync(HierarchyScenario.C1)).Should().BeEmpty();
    }

    [PostgresFact]
    public async Task AC9_y_AC10_Marca_Blanca_excluye_bloqueos_de_la_cabeza_en_hijos()
    {
        await TransitNetworkSeed.SeedMarcaBlancaNetworkAsync(Fixture);

        await using (var readBefore = NewContext())
        {
            var before = await NewResolver(readBefore).ListEffectiveOfficeIdsAsync(TransitNetworkSeed.MbHead);
            before.Should().Equal([TransitNetworkSeed.Ot1]);
        }

        await using (var ctx = NewContext())
        {
            var repo = new TenantTransitOfficeBlockRepository(ctx, NullAuditContextAccessor.Instance);
            await repo.AddBlockAsync(TransitNetworkSeed.MbHead, TransitNetworkSeed.Ot1, null, null);
        }

        await using var read = NewContext();
        var resolver = NewResolver(read);

        var head = await resolver.ListEffectiveOfficeIdsAsync(TransitNetworkSeed.MbHead);
        var child = await resolver.ListEffectiveOfficeIdsAsync(TransitNetworkSeed.MbC1);

        // Solo Ot1 tiene tenant OT operativo en la semilla base.
        head.Should().BeEmpty("el bloqueo de Ot1 deja la red MB sin OT operables");
        child.Should().Equal(head);
    }
}
