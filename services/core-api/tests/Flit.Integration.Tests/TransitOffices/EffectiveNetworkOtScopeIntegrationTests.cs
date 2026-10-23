using Flit.Admin.Application.Auditing;
using Flit.Admin.Domain.OtMetrics;
using Flit.Admin.Domain.OtQueries;
using Flit.Infrastructure.OtRules;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Repositories;
using Flit.Integration.Tests.Postgres;
using Flit.Integration.Tests.Tenancy;
using Flit.Queries.Domain;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Tramites.Estados;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Flit.Integration.Tests.TransitOffices;

/// <summary>
/// Bug #12912 (Entrega 2) — el alcance de las consultas y reportes del organismo
/// (<see cref="OtTenantScope"/>) y la salud de la bandeja cuentan a las empresas de la red con la lista
/// EFECTIVA (HU #12347), no solo a las que tienen grant propio. En la semilla todos los clientes
/// entregaron un trámite a Ot1; solo C1 tiene grant propio a Ot1 y la cabeza Concesión P lo recibe aquí.
/// <para>Uso de ejemplo:
/// <c>new OtMetricsReadRepository(ctx, resolver).ListClientCompaniesAsync(otTenantId)</c> incluye las
/// hijas de la Concesión con grant al organismo.</para>
/// </summary>
public sealed class EffectiveNetworkOtScopeIntegrationTests(PostgresDatabaseFixture fixture) : PostgresTestBase(fixture)
{
    private static EffectiveTransitOfficeListResolver NewResolver(FlitDbContext ctx) =>
        new(
            new CompanyHierarchyRepository(ctx),
            new TransitGrantRepository(ctx, NullAuditContextAccessor.Instance),
            new TenantTransitOfficeBlockRepository(ctx, NullAuditContextAccessor.Instance),
            new DbTransitOfficeOperationalStatusReader(ctx),
            NullLogger<EffectiveTransitOfficeListResolver>.Instance);

    private async Task SeedAsync(bool conMarcaBlanca = false)
    {
        if (conMarcaBlanca)
        {
            await TransitNetworkSeed.SeedMarcaBlancaNetworkAsync(Fixture);
        }
        else
        {
            await HierarchyScenario.SeedAsync(Fixture);
        }

        await using var ctx = NewContext();
        await TransitNetworkSeed.SetHeadGrantsAsync(ctx, HierarchyScenario.P, HierarchyScenario.Ot1);
    }

    private static readonly Guid[] RedConcesion = [HierarchyScenario.P, HierarchyScenario.C1, HierarchyScenario.C2];

    // (g) consultas del organismo: la fila del trámite de la hija aparece
    [PostgresFact]
    public async Task Consultas_OT_incluyen_tramite_de_hija_de_Concesion()
    {
        await SeedAsync();
        await using var ctx = NewContext();

        var result = await new OtQueryRepository(ctx, NewResolver(ctx)).ExecuteAsync(
            HierarchyScenario.O,
            new QueryRequest(
                new QueryDefinition(
                    new QueryDateFilter(OtQueryDateField.Radicacion, QueryRangePreset.Ultimos30),
                    [],
                    []),
                1,
                50));

        result.Should().NotBeNull();
        result!.Filas.Select(f => f.ClientTenantId).Should().BeEquivalentTo(RedConcesion);
        result.Filas.Should().Contain(f =>
            f.ProcedureInstanceId == HierarchyScenario.DeliveredProcedureOf(HierarchyScenario.C2));
    }

    // (g) métricas del organismo: panel y empresas cliente
    [PostgresFact]
    public async Task Metricas_OT_cuentan_tramite_de_hija_de_Concesion()
    {
        await SeedAsync();
        await using var ctx = NewContext();
        var repo = new OtMetricsReadRepository(ctx, NewResolver(ctx));

        var today = OtTenantScope.TodayInBogota();
        var panel = await repo.GetOperationalPanelAsync(
            HierarchyScenario.O, new OtMetricsFilter(today.AddDays(-30), today));

        panel.Should().NotBeNull();
        var cola = panel!.Cola;
        (cola.PorRevisar + cola.EsperandoAsignarPlaca + cola.EnEsperaDelCliente)
            .Should().Be(RedConcesion.Length, "P, C1 y C2 entregaron cada una un trámite a Ot1");
    }

    // (g) empresas cliente del organismo: la red entra con trámite recibido (Ley 1581, decisión humana)
    [PostgresFact]
    public async Task Empresas_cliente_del_OT_incluyen_red_Concesion_y_Marca_Blanca()
    {
        await SeedAsync(conMarcaBlanca: true);
        await SeedTramiteRecibidoAsync(TransitNetworkSeed.MbC1);
        await using var ctx = NewContext();

        var companies = await new OtMetricsReadRepository(ctx, NewResolver(ctx))
            .ListClientCompaniesAsync(HierarchyScenario.O);

        // MbC1 entregó un trámite a Ot1 ⇒ visible; MbHead y MbC2 pueden radicar pero no lo han hecho.
        companies.Should().NotBeNull();
        companies!.Select(c => c.TenantId).Should().BeEquivalentTo([.. RedConcesion, TransitNetworkSeed.MbC1]);
    }

    /// <summary>
    /// Bug #12912 — criterio Ley 1581: el organismo ve el NOMBRE de una compañía que entra solo por la
    /// red únicamente si ya le entregó un trámite; con grant directo se ve aunque no haya radicado.
    /// </summary>
    [PostgresFact]
    public async Task Empresas_cliente_del_OT_solo_incluyen_red_con_tramites_recibidos()
    {
        await SeedAsync(conMarcaBlanca: true);
        await using (var seed = NewContext())
        {
            // D: cliente suelto con grant directo a Ot1 y sin ningún trámite.
            seed.Tenants.Add(TenantSeed.New(DirectoSinTramites, "IT-D", isGroupParent: false, parentId: null));
            await seed.SaveChangesAsync();
            await TransitNetworkSeed.SetHeadGrantsAsync(seed, DirectoSinTramites, HierarchyScenario.Ot1);
        }

        Guid[] esperadas = [.. RedConcesion, DirectoSinTramites];

        await using var ctx = NewContext();
        var companies = await new OtMetricsReadRepository(ctx, NewResolver(ctx))
            .ListClientCompaniesAsync(HierarchyScenario.O);

        companies.Should().NotBeNull();
        companies!.Select(c => c.TenantId).Should().BeEquivalentTo(
            esperadas,
            "C2 entra por la red y entregó a Ot1; D tiene grant directo; la red Marca Blanca no ha radicado");

        var fields = await new OtQueryRepository(ctx, NewResolver(ctx)).GetFieldsAsync(HierarchyScenario.O);
        fields.Should().NotBeNull();
        fields!.Single(f => f.Id == OtQueryFieldCatalog.Empresa).Options
            .Select(o => Guid.Parse(o.Value)).Should().BeEquivalentTo(esperadas);
    }

    private static readonly Guid DirectoSinTramites = new("c0000000-0000-4000-8000-0000000000d1");

    /// <summary>Trámite entregado a Ot1 por <paramref name="tenantId"/> (con su usuario creador).</summary>
    private async Task SeedTramiteRecibidoAsync(Guid tenantId)
    {
        await using var ctx = NewContext();
        var userId = new Guid("c0000000-0002-4000-8000-0000000000e1");
        await TransitNetworkSeed.SeedUserAsync(ctx, userId, tenantId, "it-mb-tramite@flit.test");

        var type = await ctx.ProcedureTypes.AsNoTracking().OrderBy(t => t.Code).FirstAsync();
        ctx.ProcedureInstances.Add(new ProcedureInstance
        {
            Id = new Guid("c0000000-0003-4000-8000-0000000000e1"),
            TenantId = tenantId,
            ProcedureTypeId = type.Id,
            ReferenceNumber = "990001",
            Status = TramiteEstado.Entregado,
            TransitOfficeId = HierarchyScenario.Ot1,
            Plate = "MBC001",
            Vin = "VINMBC00000000001",
            CreatedByUserId = userId,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-2),
            SubmittedAt = DateTimeOffset.UtcNow.AddDays(-1),
        });
        await ctx.SaveChangesAsync();
    }

    // (h) salud de la bandeja
    [PostgresFact]
    public async Task Salud_bandeja_no_cuenta_tramite_de_hija_como_sin_grant()
    {
        await SeedAsync();
        await using var ctx = NewContext();

        var health = await new OtClientProcedureRepository(
                ctx, new NullTramiteTransitionPublisher(), effectiveOffices: NewResolver(ctx))
            .GetDeliveryHealthAsync(HierarchyScenario.O);

        health.Should().NotBeNull();
        health!.DeliveredTotal.Should().Be(HierarchyScenario.Clients.Count);
        health.DeliveredWithGrant.Should().Be(RedConcesion.Length);
        health.DeliveredWithoutGrant.Should().Be(2, "solo X y S entregaron sin pertenecer a la red de Ot1");
    }

    /// <summary>
    /// Bug #12912 (review PR #442, obs. 3) - una hija con grant directo propio que su cabeza NO respalda
    /// no puede radicar en el OT, así que tampoco aparece por nombre en la vista del organismo.
    /// </summary>
    [PostgresFact]
    public async Task Empresas_cliente_del_OT_no_incluyen_hija_con_grant_propio_no_respaldado()
    {
        await HierarchyScenario.SeedAsync(Fixture);
        await using (var seed = NewContext())
        {
            // C1 conserva su grant propio a Ot1, pero la cabeza P solo tiene Ot2.
            await TransitNetworkSeed.SetHeadGrantsAsync(seed, HierarchyScenario.P, HierarchyScenario.Ot2);
        }

        await using var ctx = NewContext();
        var companies = await new OtMetricsReadRepository(ctx, NewResolver(ctx))
            .ListClientCompaniesAsync(HierarchyScenario.O);

        companies.Should().NotBeNull();
        companies!.Should().NotContain(c => c.TenantId == HierarchyScenario.C1);

        var fields = await new OtQueryRepository(ctx, NewResolver(ctx)).GetFieldsAsync(HierarchyScenario.O);
        fields!.Single(f => f.Id == OtQueryFieldCatalog.Empresa).Options
            .Should().NotContain(o => o.Value == HierarchyScenario.C1.ToString());
    }
}
