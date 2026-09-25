using Flit.Admin.Application.Auditing;
using Flit.Infrastructure.OtRules;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Repositories;
using Flit.Integration.Tests.Postgres;
using Flit.Integration.Tests.Tenancy;
using Flit.Queries.Domain.Tenancy;
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

    // Bug #12912 (review PR #442, obs. 4) - redes hermanas no se contaminan.

    private static readonly Guid ConcesionB = new("d1000000-0000-4000-8000-00000000b001");
    private static readonly Guid ConcesionB1 = new("d2000000-0000-4000-8000-00000000b011");
    private static readonly Guid MarcaBlancaB = new("d3000000-0000-4000-8000-00000000b002");
    private static readonly Guid MarcaBlancaB1 = new("d4000000-0000-4000-8000-00000000b021");

    /// <summary>Segunda Concesión (grant solo a Ot2) y segunda Marca Blanca, además de las de la semilla.</summary>
    private async Task SeedRedesHermanasAsync()
    {
        await TransitNetworkSeed.SeedMarcaBlancaNetworkAsync(Fixture);
        await using var ctx = NewContext();
        ctx.Tenants.Add(TenantSeed.New(ConcesionB, "IT-CB", isGroupParent: true, parentId: null));
        ctx.Tenants.Add(TenantSeed.New(
            MarcaBlancaB, "IT-MBB", isGroupParent: true, parentId: null, GroupKindCodes.MarcaBlanca));
        await ctx.SaveChangesAsync();
        ctx.Tenants.Add(TenantSeed.New(ConcesionB1, "IT-CB1", isGroupParent: false, parentId: ConcesionB));
        ctx.Tenants.Add(TenantSeed.New(MarcaBlancaB1, "IT-MBB1", isGroupParent: false, parentId: MarcaBlancaB));
        await ctx.SaveChangesAsync();

        await TransitNetworkSeed.SetHeadGrantsAsync(ctx, HierarchyScenario.P, HierarchyScenario.Ot1);
        await TransitNetworkSeed.SetHeadGrantsAsync(ctx, ConcesionB, HierarchyScenario.Ot2);
    }

    [PostgresFact]
    public async Task Bug12912_Concesiones_hermanas_no_comparten_OT()
    {
        await SeedRedesHermanasAsync();
        await using var read = NewContext();
        var resolver = NewResolver(read);

        (await resolver.ListEffectiveOfficeIdsAsync(ConcesionB1)).Should().Equal([HierarchyScenario.Ot2]);
        (await resolver.ListEffectiveOfficeIdsAsync(HierarchyScenario.C2)).Should().Equal([HierarchyScenario.Ot1]);

        var ot1 = await resolver.ListEffectiveTenantIdsForOfficeAsync(HierarchyScenario.Ot1);
        ot1.Should().Contain([HierarchyScenario.P, HierarchyScenario.C1, HierarchyScenario.C2]);
        ot1.Should().NotContain([ConcesionB, ConcesionB1]);

        var ot2 = await resolver.ListEffectiveTenantIdsForOfficeAsync(HierarchyScenario.Ot2);
        ot2.Should().Contain([ConcesionB, ConcesionB1]);
        ot2.Should().NotContain([HierarchyScenario.P, HierarchyScenario.C1, HierarchyScenario.C2]);
    }

    [PostgresTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Bug12912_bloqueo_de_una_Marca_Blanca_no_afecta_a_su_hermana(bool bloqueaLaSemilla)
    {
        await SeedRedesHermanasAsync();
        var (bloqueada, hijaBloqueada, libre, hijaLibre) = bloqueaLaSemilla
            ? (TransitNetworkSeed.MbHead, TransitNetworkSeed.MbC1, MarcaBlancaB, MarcaBlancaB1)
            : (MarcaBlancaB, MarcaBlancaB1, TransitNetworkSeed.MbHead, TransitNetworkSeed.MbC1);

        await using (var ctx = NewContext())
        {
            await new TenantTransitOfficeBlockRepository(ctx, NullAuditContextAccessor.Instance)
                .AddBlockAsync(bloqueada, HierarchyScenario.Ot1, null, null);
        }

        await using var read = NewContext();
        var resolver = NewResolver(read);

        (await resolver.ListEffectiveOfficeIdsAsync(hijaBloqueada)).Should().BeEmpty();
        (await resolver.ListEffectiveOfficeIdsAsync(hijaLibre)).Should().Equal([HierarchyScenario.Ot1]);

        var ot1 = await resolver.ListEffectiveTenantIdsForOfficeAsync(HierarchyScenario.Ot1);
        ot1.Should().Contain([libre, hijaLibre]);
        ot1.Should().NotContain([bloqueada, hijaBloqueada]);
    }
}
