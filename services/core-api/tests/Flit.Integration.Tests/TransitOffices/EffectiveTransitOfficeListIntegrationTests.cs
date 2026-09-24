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

    /// <summary>
    /// Bug #12912 — propiedad de coherencia directo/inverso: para todo tenant del escenario y todo OT,
    /// <c>officeId ∈ Effective(tenant) ⇔ tenant ∈ Inverse(officeId)</c>. Se recorre con y sin grants de
    /// la cabeza Concesión y con y sin bloqueo de la cabeza Marca Blanca.
    /// </summary>
    [PostgresTheory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task Bug12912_Inverso_es_coherente_con_la_lista_efectiva(bool concesionConGrants, bool mbBloqueaOt1)
    {
        await TransitNetworkSeed.SeedMarcaBlancaNetworkAsync(Fixture);
        await using (var ctx = NewContext())
        {
            if (concesionConGrants)
            {
                await TransitNetworkSeed.SetHeadGrantsAsync(
                    ctx, HierarchyScenario.P, HierarchyScenario.Ot1, HierarchyScenario.Ot2);
            }

            if (mbBloqueaOt1)
            {
                await new TenantTransitOfficeBlockRepository(ctx, NullAuditContextAccessor.Instance)
                    .AddBlockAsync(TransitNetworkSeed.MbHead, TransitNetworkSeed.Ot1, null, null);
            }
        }

        Guid[] tenants =
        [
            HierarchyScenario.P, HierarchyScenario.C1, HierarchyScenario.C2, HierarchyScenario.X,
            HierarchyScenario.S, HierarchyScenario.O,
            TransitNetworkSeed.MbHead, TransitNetworkSeed.MbC1, TransitNetworkSeed.MbC2,
        ];
        Guid[] offices = [HierarchyScenario.Ot1, HierarchyScenario.Ot2];

        await using var read = NewContext();
        var resolver = NewResolver(read);

        var forward = new Dictionary<Guid, IReadOnlyList<Guid>>();
        foreach (var tenant in tenants)
        {
            forward[tenant] = await resolver.ListEffectiveOfficeIdsAsync(tenant);
        }

        foreach (var office in offices)
        {
            var inverse = await resolver.ListEffectiveTenantIdsForOfficeAsync(office);
            var expected = tenants.Where(t => forward[t].Contains(office)).ToList();

            inverse.Should().BeEquivalentTo(
                expected,
                $"el inverso de {office} debe ser exactamente los tenants cuya lista efectiva lo contiene");
        }
    }

    /// <summary>Bug #12912 — valores concretos del inverso sobre la red (complementa la propiedad).</summary>
    [PostgresFact]
    public async Task Bug12912_Inverso_incluye_hijas_de_Concesion_y_red_Marca_Blanca()
    {
        await TransitNetworkSeed.SeedMarcaBlancaNetworkAsync(Fixture);
        await using (var ctx = NewContext())
        {
            await TransitNetworkSeed.SetHeadGrantsAsync(ctx, HierarchyScenario.P, HierarchyScenario.Ot1);
        }

        await using var read = NewContext();
        var inverse = await NewResolver(read).ListEffectiveTenantIdsForOfficeAsync(HierarchyScenario.Ot1);

        inverse.Should().BeEquivalentTo(
        [
            HierarchyScenario.P, HierarchyScenario.C1, HierarchyScenario.C2,
            TransitNetworkSeed.MbHead, TransitNetworkSeed.MbC1, TransitNetworkSeed.MbC2,
        ]);
    }
}
