using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Repositories;
using Flit.Integration.Tests.Postgres;
using Flit.Integration.Tests.Tenancy;
using Flit.Queries.Domain.Tenancy;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Estados;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Flit.Integration.Tests.Network;

/// <summary>
/// HU #12358 (Feature #12257, épica #12235) — lectura consolidada de la red contra PostgreSQL real
/// sobre <see cref="HierarchyScenario"/> (P cabeza; C1/C2 hijos; X ajeno; S aislado), ejercitando los
/// handlers <c>Network*</c> y las sobrecargas del repositorio con <see cref="TenantScope"/>:
/// <list type="bullet">
///   <item>AC1 — P obtiene lo propio + lo de C1 y C2, cada fila con su dueño; el detalle consolidado
///   trae los mismos campos que el detalle propio del hijo.</item>
///   <item>AC2 — X nunca aparece; C1 (Single) no ve a C2.</item>
///   <item>AC3 — conjunto de lectura vacío ⇒ cero filas, nunca «sin filtro».</item>
///   <item>AC8 — desvincular a C1 en BD entre dos peticiones ⇒ la segunda no devuelve filas de C1.</item>
///   <item>AC9 — S obtiene por las rutas viejas exactamente lo mismo que antes (sobrecarga <c>Guid?</c> intacta).</item>
///   <item>AC10 — página acotada en servidor; sin paginación ⇒ primera página, nunca el conjunto completo.</item>
/// </list>
/// Uso de ejemplo: <c>await HierarchyScenario.SeedAsync(Fixture)</c>; luego
/// <c>new NetworkListProcedureInstancesHandler(new ProcedureInstanceRepository(ctx)).HandleAsync(scope, null, new())</c>.
/// </summary>
public sealed class NetworkProceduresReadTests(PostgresDatabaseFixture fixture) : PostgresTestBase(fixture)
{
    private static TenantScope GroupP() =>
        TenantScope.Group(HierarchyScenario.P, [HierarchyScenario.C1, HierarchyScenario.C2], GroupKind.Concesion);

    private static ProcedureInstanceListRequest Page(int? take = null, int skip = 0) =>
        new() { Skip = skip, Take = take ?? ListProcedureInstancesHandler.MaxItems };

    // ── AC1 — listado consolidado + detalle ───────────────────────────────────────────────────

    [PostgresFact]
    public async Task AC1_Cabeza_lista_lo_propio_y_lo_de_sus_dos_hijos_con_el_dueno_en_cada_fila()
    {
        await HierarchyScenario.SeedAsync(Fixture);
        await using var ctx = NewContext();
        var handler = new NetworkListProcedureInstancesHandler(new ProcedureInstanceRepository(ctx));

        var (items, total, error) = await handler.HandleAsync(GroupP(), childTenantId: null, Page());

        error.Should().BeNull();
        total.Should().Be(6);
        items.Select(i => i.Id).Should().BeEquivalentTo(
            HierarchyScenario.ProceduresOf(HierarchyScenario.P)
                .Concat(HierarchyScenario.ProceduresOf(HierarchyScenario.C1))
                .Concat(HierarchyScenario.ProceduresOf(HierarchyScenario.C2)));
        // Cada fila dice a qué cliente pertenece (id + razón social resuelta en lote).
        items.Should().OnlyContain(i => i.TenantId != Guid.Empty && !string.IsNullOrWhiteSpace(i.CompaniaNombre));
        items.Where(i => i.TenantId == HierarchyScenario.C1).Should().HaveCount(2)
            .And.OnlyContain(i => i.CompaniaNombre!.Contains("IT-C1"));
        LeakAssert.NoForeignRows("network/instances Group(P)", HierarchyScenario.P, GroupP().ReadTenantIds, items, i => i.TenantId, i => i.Id);
    }

    [PostgresFact]
    public async Task AC1_Filtro_por_hijo_acota_al_hijo_y_por_cabeza_a_lo_propio()
    {
        await HierarchyScenario.SeedAsync(Fixture);
        await using var ctx = NewContext();
        var handler = new NetworkListProcedureInstancesHandler(new ProcedureInstanceRepository(ctx));

        var (soloC2, totalC2, _) = await handler.HandleAsync(GroupP(), HierarchyScenario.C2, Page());
        var (soloP, totalP, _) = await handler.HandleAsync(GroupP(), HierarchyScenario.P, Page());

        totalC2.Should().Be(2);
        soloC2.Select(i => i.Id).Should().BeEquivalentTo(HierarchyScenario.ProceduresOf(HierarchyScenario.C2));
        totalP.Should().Be(2);
        soloP.Should().OnlyContain(i => i.TenantId == HierarchyScenario.P);
    }

    [PostgresFact]
    public async Task AC1_Filtros_de_estado_y_organismo_aplican_sobre_la_red()
    {
        await HierarchyScenario.SeedAsync(Fixture);
        await using var ctx = NewContext();
        var repo = new ProcedureInstanceRepository(ctx);
        var handler = new NetworkListProcedureInstancesHandler(repo);
        var counts = new NetworkCountProcedureInstancesByStatusHandler(repo);

        var (borradores, totalBorradores, _) = await handler.HandleAsync(
            GroupP(), null, Page() with { Estados = [TramiteEstado.Borrador] });
        var (conteos, errorConteos) = await counts.HandleAsync(GroupP(), null, Page());
        var (porFecha, totalFecha, _) = await handler.HandleAsync(
            GroupP(), null, Page() with { CreatedFrom = DateTimeOffset.UtcNow.AddDays(-1).AddHours(-1) });
        var (organismo, totalOrganismo, _) = await handler.HandleAsync(
            GroupP(), null, Page() with { OrganismoTransito = "NINGUN-ORGANISMO-SEMBRADO" });

        totalBorradores.Should().Be(3);
        borradores.Should().OnlyContain(i => i.Estado == TramiteEstado.Borrador);
        errorConteos.Should().BeNull();
        conteos![TramiteEstado.Borrador].Should().Be(3);
        conteos[TramiteEstado.Entregado].Should().Be(3);
        conteos.Keys.Should().Contain(TramiteEstado.Todos);
        totalFecha.Should().Be(3, "solo los borradores se crearon hace un día; los entregados hace dos");
        porFecha.Should().OnlyContain(i => i.Estado == TramiteEstado.Borrador);
        totalOrganismo.Should().Be(0);
        organismo.Should().BeEmpty();
    }

    [PostgresFact]
    public async Task AC1_Detalle_consolidado_trae_los_mismos_campos_que_el_detalle_propio_del_hijo()
    {
        await HierarchyScenario.SeedAsync(Fixture);
        await using var ctx = NewContext();
        var repo = new ProcedureInstanceRepository(ctx);
        var id = HierarchyScenario.DeliveredProcedureOf(HierarchyScenario.C1);

        var (network, networkError) = await new NetworkGetProcedureInstanceHandler(repo).HandleAsync(id, GroupP());
        var (own, ownError) = await new GetProcedureInstanceHandler(repo).HandleAsync(id, HierarchyScenario.C1);

        networkError.Should().BeNull();
        ownError.Should().BeNull();
        network!.TenantId.Should().Be(HierarchyScenario.C1);
        network.TenantName.Should().Contain("IT-C1");
        // Mismos campos funcionales, en solo lectura: el DTO anidado es el MISMO tipo y el mismo valor.
        network.Instance.Should().BeEquivalentTo(own);
        network.Instance.StatusHistory.Should().HaveCount(2);
    }

    // ── AC2 — el ajeno nunca; el hermano tampoco ──────────────────────────────────────────────

    [PostgresFact]
    public async Task AC2_Ningun_lector_de_la_red_obtiene_filas_de_X_y_C1_no_ve_a_C2()
    {
        await HierarchyScenario.SeedAsync(Fixture);
        await using var ctx = NewContext();
        var repo = new ProcedureInstanceRepository(ctx);
        var list = new NetworkListProcedureInstancesHandler(repo);
        var detail = new NetworkGetProcedureInstanceHandler(repo);

        var (deP, _, _) = await list.HandleAsync(GroupP(), null, Page());
        deP.Should().NotContain(i => i.TenantId == HierarchyScenario.X)
            .And.NotContain(i => i.TenantId == HierarchyScenario.S);

        // childTenantId ajeno ⇒ 403 en memoria, sin consulta (ninguna fila).
        var (deX, totalX, errorX) = await list.HandleAsync(GroupP(), HierarchyScenario.X, Page());
        errorX.Should().Be(NetworkScopePolicy.ChildOutOfScope);
        deX.Should().BeEmpty();
        totalX.Should().Be(0);

        var (detalleX, errorDetalleX) = await detail.HandleAsync(HierarchyScenario.DeliveredProcedureOf(HierarchyScenario.X), GroupP());
        detalleX.Should().BeNull();
        errorDetalleX.Should().Be("not_found");

        // C1 es Single: la policy de cabeza lo rechaza en la ruta consolidada…
        var single = await NewResolver(ctx).ResolveAsync(HierarchyScenario.C1);
        var (deC1, _, errorC1) = await list.HandleAsync(single, null, Page());
        errorC1.Should().Be(NetworkScopePolicy.ScopeRequired);
        deC1.Should().BeEmpty();

        // …y aunque el repositorio se llamara con su alcance, jamás vería a su hermano C2.
        var (filasC1, _) = await repo.ListWithSummaryGraphFilteredAsync(
            single, 0, 100, new(), ProcedureInstanceSortBy.Default, SortDirection.Descending, CancellationToken.None);
        LeakAssert.NoForeignRows("network Single(C1)", HierarchyScenario.C1, single.ReadTenantIds, filasC1, i => i.TenantId, i => i.Id);
        filasC1.Should().NotContain(i => i.TenantId == HierarchyScenario.C2);
    }

    // ── AC3 — conjunto de lectura vacío ⇒ cero filas ──────────────────────────────────────────

    [PostgresFact]
    public async Task AC3_Conjunto_de_lectura_vacio_devuelve_cero_filas_en_listado_conteo_y_detalle()
    {
        await HierarchyScenario.SeedAsync(Fixture);
        await using var ctx = NewContext();
        var repo = new ProcedureInstanceRepository(ctx);
        // Único modo de obtener un conjunto vacío sin All(): un tenant que no tiene ninguna fila.
        var vacio = TenantScope.Single(new Guid("a0000000-0000-4000-8000-00000000dead"));

        var (filas, total) = await repo.ListWithSummaryGraphFilteredAsync(
            vacio, 0, 100, new(), ProcedureInstanceSortBy.Default, SortDirection.Descending, CancellationToken.None);
        var conteos = await repo.CountByStatusFilteredAsync(vacio, new(), CancellationToken.None);
        var detalle = await repo.GetByIdWithDetailsAsync(HierarchyScenario.DeliveredProcedureOf(HierarchyScenario.P), vacio, CancellationToken.None);

        filas.Should().BeEmpty("un conjunto de lectura sin datos es cero filas, no ausencia de filtro");
        total.Should().Be(0);
        conteos.Values.Sum().Should().Be(0);
        detalle.Should().BeNull();

        // Y el total del escenario NO es cero: la consulta sin alcance sí tendría filas.
        (await ctx.ProcedureInstances.CountAsync()).Should().Be(10);
    }

    // ── AC8 — desvínculo efectivo en la siguiente petición ────────────────────────────────────

    [PostgresFact]
    public async Task AC8_Tras_desvincular_a_C1_la_siguiente_peticion_no_devuelve_sus_filas()
    {
        await HierarchyScenario.SeedAsync(Fixture);

        // Petición 1: P ve a C1.
        await using (var ctx = NewContext())
        {
            var scope = await NewResolver(ctx).ResolveAsync(HierarchyScenario.P);
            var (antes, _, _) = await new NetworkListProcedureInstancesHandler(new ProcedureInstanceRepository(ctx))
                .HandleAsync(scope, null, Page());
            antes.Should().Contain(i => i.TenantId == HierarchyScenario.C1);
        }

        // SuperAdmin desvincula a C1 en la base (sin tocar token ni sesión).
        await using (var admin = NewContext())
        {
            var c1 = await admin.Tenants.SingleAsync(t => t.Id == HierarchyScenario.C1);
            c1.ParentTenantId = null;
            await admin.SaveChangesAsync();
        }

        // Petición 2: el alcance se resuelve de nuevo por petición (sin caché) y C1 ya no está.
        await using (var ctx = NewContext())
        {
            var scope = await NewResolver(ctx).ResolveAsync(HierarchyScenario.P);
            scope.ReadTenantIds.Should().NotContain(HierarchyScenario.C1);
            var (despues, total, error) = await new NetworkListProcedureInstancesHandler(new ProcedureInstanceRepository(ctx))
                .HandleAsync(scope, null, Page());
            error.Should().BeNull();
            total.Should().Be(4);
            despues.Should().NotContain(i => i.TenantId == HierarchyScenario.C1);
            despues.Should().Contain(i => i.TenantId == HierarchyScenario.C2);

            var (detalle, detalleError) = await new NetworkGetProcedureInstanceHandler(new ProcedureInstanceRepository(ctx))
                .HandleAsync(HierarchyScenario.DeliveredProcedureOf(HierarchyScenario.C1), scope);
            detalle.Should().BeNull();
            detalleError.Should().Be("not_found");
        }
    }

    // ── AC9 — cliente sin jerarquía: las rutas viejas responden igual ─────────────────────────

    [PostgresFact]
    public async Task AC9_Cliente_aislado_obtiene_lo_mismo_por_la_sobrecarga_Guid_y_por_la_de_alcance()
    {
        await HierarchyScenario.SeedAsync(Fixture);
        await using var ctx = NewContext();
        var repo = new ProcedureInstanceRepository(ctx);
        var single = await NewResolver(ctx).ResolveAsync(HierarchyScenario.S);
        single.IsGroup.Should().BeFalse();

        // Rutas viejas (sobrecarga Guid? intacta) — lo que S obtiene hoy.
        var (viejas, totalViejas) = await new ListProcedureInstancesFilteredHandler(repo)
            .HandleAsync(new ProcedureInstanceListRequest { TenantId = HierarchyScenario.S });
        var (detalleViejo, _) = await new GetProcedureInstanceHandler(repo)
            .HandleAsync(HierarchyScenario.DeliveredProcedureOf(HierarchyScenario.S), HierarchyScenario.S);

        // Sobrecarga nueva con Single(S): mismo conjunto.
        var (nuevas, totalNuevas) = await repo.ListWithSummaryGraphFilteredAsync(
            single, 0, ListProcedureInstancesHandler.MaxItems, new(), ProcedureInstanceSortBy.Default, SortDirection.Descending, CancellationToken.None);

        totalViejas.Should().Be(2);
        totalNuevas.Should().Be(totalViejas);
        nuevas.Select(i => i.Id).Should().BeEquivalentTo(viejas.Select(i => i.Id));
        detalleViejo.Should().NotBeNull();
        detalleViejo!.TenantId.Should().Be(HierarchyScenario.S);

        // La ruta consolidada NO es para S (sin red): la policy lo rechaza sin consultar.
        var (_, _, error) = await new NetworkListProcedureInstancesHandler(repo).HandleAsync(single, null, Page());
        error.Should().Be(NetworkScopePolicy.ScopeRequired);
    }

    // ── AC10 — paginación obligatoria con tope en servidor ────────────────────────────────────

    [PostgresFact]
    public async Task AC10_Sin_paginacion_devuelve_la_primera_pagina_con_el_tope_del_servidor()
    {
        var type = await HierarchyScenario.SeedAsync(Fixture);
        const int extra = 205;
        await using (var seed = NewContext())
        {
            SeedExtraDrafts(seed, HierarchyScenario.C1, type.Id, extra);
            await seed.SaveChangesAsync();
        }

        await using var ctx = NewContext();
        var handler = new NetworkListProcedureInstancesHandler(new ProcedureInstanceRepository(ctx));

        // Sin paginación explícita (Take por defecto) y con un take desmesurado: ambos caen al tope.
        var (sinPaginar, total, _) = await handler.HandleAsync(GroupP(), null, new ProcedureInstanceListRequest());
        var (takeGrande, _, _) = await handler.HandleAsync(GroupP(), null, Page(take: 10_000));
        var (segunda, _, _) = await handler.HandleAsync(GroupP(), null, Page(take: ListProcedureInstancesHandler.MaxItems, skip: ListProcedureInstancesHandler.MaxItems));
        var (pequena, _, _) = await handler.HandleAsync(GroupP(), null, Page(take: 3));

        total.Should().Be(6 + extra);
        sinPaginar.Should().HaveCount(ListProcedureInstancesHandler.MaxItems);
        takeGrande.Should().HaveCount(ListProcedureInstancesHandler.MaxItems);
        segunda.Should().HaveCount(6 + extra - ListProcedureInstancesHandler.MaxItems);
        pequena.Should().HaveCount(3);
        LeakAssert.NoForeignRows("network/instances página", HierarchyScenario.P, GroupP().ReadTenantIds, sinPaginar, i => i.TenantId, i => i.Id);
    }

    [Fact]
    public void AC10_El_tope_de_pagina_se_acota_en_servidor()
    {
        ListProcedureInstancesFilteredHandler.ClampTake(0).Should().Be(ListProcedureInstancesHandler.MaxItems);
        ListProcedureInstancesFilteredHandler.ClampTake(-5).Should().Be(ListProcedureInstancesHandler.MaxItems);
        ListProcedureInstancesFilteredHandler.ClampTake(10_000).Should().Be(ListProcedureInstancesHandler.MaxItems);
        ListProcedureInstancesFilteredHandler.ClampTake(25).Should().Be(25);
        new ProcedureInstanceListRequest().Take.Should().Be(ListProcedureInstancesHandler.MaxItems, "sin paginación ⇒ primera página");
    }

    // ── helpers ──────────────────────────────────────────────────────────────────────────────

    private static void SeedExtraDrafts(FlitDbContext ctx, Guid tenant, Guid typeId, int count)
    {
        var user = HierarchyScenario.UserOf(tenant);
        for (var n = 0; n < count; n++)
        {
            ctx.ProcedureInstances.Add(new ProcedureInstance
            {
                Id = Guid.NewGuid(),
                TenantId = tenant,
                ProcedureTypeId = typeId,
                ReferenceNumber = $"NET{n:D6}",
                Status = TramiteEstado.Borrador,
                Plate = $"NET{n % 1000:D3}",
                CreatedByUserId = user,
                CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-n),
            });
        }
    }

    private static DbTenantScopeResolver NewResolver(FlitDbContext ctx) =>
        new(ctx, new DbHierarchySwitches(ctx, NullLogger<DbHierarchySwitches>.Instance), NullLogger<DbTenantScopeResolver>.Instance);
}
