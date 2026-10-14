using Flit.Admin.Application.OtClientProcedures.ListOtClientProcedures;
using Flit.Admin.Domain.OtClientProcedures;
using Flit.Api.Endpoints;
using Flit.Infrastructure.ConsolidadoLotes.Ot;
using Flit.Infrastructure.Persistence.Repositories;
using Flit.Integration.Tests.Postgres;
using Flit.Integration.Tests.Tenancy;
using Flit.Queries.Domain;
using Flit.Tramites.Application.UseCases.ConsolidadoLotes;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Estados;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Flit.Integration.Tests.TransitOffices;

/// <summary>
/// HU #13390 (Épica #13216) — «Seleccionar todos» del lote de consolidados desde la bandeja del OT, contra
/// PostgreSQL real: <see cref="OtBandejaSeleccionResolver"/> sobre
/// <see cref="OtClientProcedureRepository.ListAccessibleRefsAsync"/>. Escenario base
/// <see cref="HierarchyScenario"/> (cinco clientes con un trámite entregado a <c>Ot1</c> y un borrador; el
/// tenant <c>O</c> es el organismo de <c>Ot1</c>), como el AC7 de la HU #12350. Cubre Q50 del inventario
/// #12322 (lectura cross-tenant nueva).
/// <para>Uso de ejemplo:
/// <code>
/// var refs = await new OtBandejaSeleccionResolver(repo).ResolverAsync(
///     new SeleccionPorFiltro(new OtBandejaLoteFiltro(body.ToFilter()), excluidos),
///     new LoteSeleccionContexto(HierarchyScenario.O, usuario), ct);
/// </code></para>
/// </summary>
public sealed class OtBandejaSeleccionResolverIntegrationTests(PostgresDatabaseFixture fixture) : PostgresTestBase(fixture)
{
    private static readonly Guid Usuario = HierarchyScenario.UserOf(HierarchyScenario.C1);

    private static LoteSeleccionContexto Contexto(Guid? otTenant = null, Guid? organismo = null) =>
        new(otTenant ?? HierarchyScenario.O, Usuario, organismo);

    private static SeleccionPorFiltro PorFiltro(OtBandejaSearchRequest body, IReadOnlyList<Guid>? excluidos = null) =>
        new(new OtBandejaLoteFiltro(body.ToFilter()), excluidos);

    private async Task<IReadOnlyList<ProcedureInstanceRef>> ResolverAsync(
        LoteSeleccion seleccion, LoteSeleccionContexto contexto)
    {
        await using var ctx = NewContext();
        var resolver = new OtBandejaSeleccionResolver(new OtClientProcedureRepository(ctx, new NullTramiteTransitionPublisher()));
        return await resolver.ResolverAsync(seleccion, contexto, TestContext.Current.CancellationToken);
    }

    /// <summary>Lo que devuelve <c>POST /api/v1/admin/ot/client-procedures/search</c> con el mismo cuerpo (handler real).</summary>
    private async Task<ListOtClientProceduresResult> BandejaAsync(OtBandejaSearchRequest body)
    {
        await using var ctx = NewContext();
        var handler = new ListOtClientProceduresHandler(new OtClientProcedureRepository(ctx, new NullTramiteTransitionPublisher()));
        return await handler.HandleAsync(body.ToQuery(HierarchyScenario.O, null), TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// Siembra trámites adicionales del cliente <paramref name="cliente"/> (usuario ya sembrado por el escenario).
    /// Radicados en el bloque 7xxxxx, fuera del rango de <see cref="HierarchyScenario.ReferenceOf"/>.
    /// </summary>
    private async Task<IReadOnlyList<Guid>> SembrarAsync(
        Guid cliente, int cuantos, Func<int, string> placa, int desde = 0,
        string estado = TramiteEstado.Entregado, Guid? organismo = null, bool borrado = false)
    {
        await using var ctx = NewContext();
        var type = await ctx.ProcedureTypes.AsNoTracking().OrderBy(t => t.Code).Select(t => t.Id).FirstAsync();
        var now = DateTimeOffset.UtcNow;
        var ids = new List<Guid>();
        for (var i = 0; i < cuantos; i++)
        {
            var n = desde + i;
            var p = new ProcedureInstance
            {
                Id = Guid.NewGuid(),
                TenantId = cliente,
                ProcedureTypeId = type,
                ReferenceNumber = $"{700000 + n}",
                Status = estado,
                TransitOfficeId = organismo ?? HierarchyScenario.Ot1,
                Plate = placa(n),
                Vin = $"VINLOTE{n:D10}",
                CreatedByUserId = HierarchyScenario.UserOf(cliente),
                CreatedAt = now.AddMinutes(-n - 10),
                SubmittedAt = now.AddMinutes(-n - 5),
                DeletedAt = borrado ? now : null,
            };
            ctx.ProcedureInstances.Add(p);
            ids.Add(p.Id);
        }

        await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);
        return ids;
    }

    [PostgresFact]
    public async Task AC1_lista_pegada_de_placas_da_lo_mismo_que_la_bandeja_y_en_su_orden()
    {
        await HierarchyScenario.SeedAsync(Fixture);
        // 5 del escenario + 25 = 30 trámites recibidos por Ot1, repartidos entre dos compañías cliente.
        await SembrarAsync(HierarchyScenario.C1, 13, n => $"LP{n:D4}");
        await SembrarAsync(HierarchyScenario.C2, 12, n => $"LP{n:D4}", desde: 13);

        // 12 placas pegadas: 10 en la bandeja, la del borrador de X (fuera) y una que no existe.
        var pegadas = Enumerable.Range(3, 10).Select(n => $"LP{n:D4}").Concat(["X999", "ZZZ999"]).ToList();
        var body = new OtBandejaSearchRequest
        {
            Condiciones = [new QueryCondition("placa", QueryOperator.EsAlguno, pegadas)],
            SortBy = "placa",
            SortDir = "desc",
            Page = 1,
            PageSize = 100,
        };

        var bandeja = await BandejaAsync(body);
        var refs = await ResolverAsync(PorFiltro(body), Contexto());

        refs.Should().HaveCount(10);
        bandeja.TotalCount.Should().Be(refs.Count);
        refs.Select(r => r.Id).Should().Equal(bandeja.Data.Select(d => d.Id), "el orden es el de la bandeja");
        refs.Select(r => r.Plate).Should().BeInDescendingOrder();
        refs.Should().OnlyContain(r => r.ReferenceNumber.Length > 0 && r.Plate != null && r.Plate.StartsWith("LP"));
    }

    [PostgresFact]
    public async Task AC2_filtro_de_250_con_3_excluidos_da_247_sin_el_tope_de_pagina()
    {
        await HierarchyScenario.SeedAsync(Fixture);
        var ids = await SembrarAsync(HierarchyScenario.X, 250, n => $"QQ{n:D4}");
        var body = new OtBandejaSearchRequest { Placa = "QQ", Page = 1, PageSize = 100 };

        var bandeja = await BandejaAsync(body);
        var excluidos = new[] { ids[0], ids[125], ids[249] };
        var refs = await ResolverAsync(PorFiltro(body, excluidos), Contexto());

        bandeja.TotalCount.Should().Be(250);
        bandeja.Data.Should().HaveCount(ListOtClientProceduresHandler.MaxPageSize);
        refs.Should().HaveCount(247);
        refs.Select(r => r.Id).Should().NotIntersectWith(excluidos).And.OnlyHaveUniqueItems();
    }

    [PostgresFact]
    public async Task AC3_ids_inyectados_fuera_de_la_bandeja_quedan_fuera()
    {
        await HierarchyScenario.SeedAsync(Fixture);
        var deOtroOrganismo = (await SembrarAsync(HierarchyScenario.C1, 1, _ => "OTR001", organismo: HierarchyScenario.Ot2))[0];
        var borrado = (await SembrarAsync(HierarchyScenario.C2, 1, _ => "BOR001", desde: 1, borrado: true))[0];
        var deLaBandeja = new[]
        {
            HierarchyScenario.DeliveredProcedureOf(HierarchyScenario.C1),
            HierarchyScenario.DeliveredProcedureOf(HierarchyScenario.X),
        };
        var seleccion = new SeleccionPorIds(
        [
            deLaBandeja[0], deLaBandeja[1], deOtroOrganismo,
            HierarchyScenario.DraftProcedureOf(HierarchyScenario.S), borrado,
        ]);

        var refs = await ResolverAsync(seleccion, Contexto());

        refs.Select(r => r.Id).Should().BeEquivalentTo(deLaBandeja);
        refs.Should().HaveCount(2, "el total que se congela es 2");
    }

    [PostgresFact]
    public async Task AC4_revocar_el_grant_no_saca_tramites_ya_entregados()
    {
        await HierarchyScenario.SeedAsync(Fixture);
        await using (var revokeCtx = NewContext())
        {
            await revokeCtx.TenantTransitOfficeGrants
                .Where(g => g.TenantId == HierarchyScenario.C1 && g.TransitOfficeId == HierarchyScenario.Ot1)
                .ExecuteDeleteAsync(TestContext.Current.CancellationToken);
        }

        var refs = await ResolverAsync(PorFiltro(new OtBandejaSearchRequest()), Contexto());

        refs.Select(r => r.Id).Should().Contain(HierarchyScenario.DeliveredProcedureOf(HierarchyScenario.C1));
        refs.Select(r => r.Id).Should().BeEquivalentTo(HierarchyScenario.Clients.Select(HierarchyScenario.DeliveredProcedureOf));
    }

    [PostgresFact]
    public async Task AC5_tenant_sin_organismo_resoluble_no_lee_nada()
    {
        await HierarchyScenario.SeedAsync(Fixture);
        var todos = HierarchyScenario.Clients.SelectMany(HierarchyScenario.ProceduresOf).ToList();

        // X es un cliente sin perfil de organismo; el otro, un tenant que no existe.
        foreach (var sinOrganismo in new[] { HierarchyScenario.X, Guid.NewGuid() })
        {
            var porFiltro = await ResolverAsync(PorFiltro(new OtBandejaSearchRequest()), Contexto(sinOrganismo));
            var porIds = await ResolverAsync(new SeleccionPorIds(todos), Contexto(sinOrganismo));

            porFiltro.Should().BeEmpty();
            porIds.Should().BeEmpty();
        }
    }

    [PostgresFact]
    public async Task Organismo_elegido_por_el_super_admin_acota_al_de_ese_organismo()
    {
        await HierarchyScenario.SeedAsync(Fixture);
        var enOt2 = (await SembrarAsync(HierarchyScenario.C2, 1, _ => "OTR002", organismo: HierarchyScenario.Ot2))[0];

        var refs = await ResolverAsync(PorFiltro(new OtBandejaSearchRequest()), Contexto(organismo: HierarchyScenario.Ot2));

        refs.Should().ContainSingle().Which.Id.Should().Be(enOt2);
    }

    [PostgresFact]
    public async Task AC6_cada_referencia_lleva_la_compania_cliente_del_tramite()
    {
        await HierarchyScenario.SeedAsync(Fixture);

        // El radicado lo reescribe la base al insertar (consecutivo FTn-…): se compara con el persistido.
        Dictionary<Guid, string> radicados;
        await using (var ctx = NewContext())
        {
            radicados = await ctx.ProcedureInstances.AsNoTracking()
                .Where(p => p.TransitOfficeId == HierarchyScenario.Ot1)
                .ToDictionaryAsync(p => p.Id, p => p.ReferenceNumber, TestContext.Current.CancellationToken);
        }

        var refs = await ResolverAsync(PorFiltro(new OtBandejaSearchRequest()), Contexto());

        refs.Should().HaveCount(HierarchyScenario.Clients.Count);
        foreach (var cliente in HierarchyScenario.Clients)
        {
            var id = HierarchyScenario.DeliveredProcedureOf(cliente);
            refs.Should().ContainSingle(r => r.Id == id)
                .Which.Should().Be(new ProcedureInstanceRef(id, cliente, radicados[id], HierarchyScenario.SharedPlate));
        }

        refs.Should().NotContain(r => r.TenantId == HierarchyScenario.O);
    }

    /// <summary>
    /// Code review épica #13216 (Obs2) — contra PostgreSQL real (lectura cross-tenant de la bandeja): el límite corta en
    /// SQL tras el orden de la bandeja (prefijo de la lectura entera) y el conteo del 422 usa el mismo universo y filtro,
    /// restando solo los excluidos que estaban en la bandeja. Sin organismo resoluble el conteo es 0.
    /// </summary>
    [PostgresFact]
    public async Task Obs2_el_limite_corta_en_SQL_en_el_orden_de_la_bandeja_y_el_conteo_usa_el_mismo_predicado()
    {
        await HierarchyScenario.SeedAsync(Fixture);
        var ids = await SembrarAsync(HierarchyScenario.C1, 9, n => $"LM{n:D4}");
        await SembrarAsync(HierarchyScenario.C2, 3, n => $"LM{n:D4}", desde: 9, borrado: true);
        var body = new OtBandejaSearchRequest { Placa = "LM", SortBy = "placa", SortDir = "asc", Page = 1, PageSize = 100 };
        var excluidos = new[] { ids[0], ids[4], Guid.NewGuid() };
        var ct = TestContext.Current.CancellationToken;

        await using var ctx = NewContext();
        var resolver = new OtBandejaSeleccionResolver(new OtClientProcedureRepository(ctx, new NullTramiteTransitionPublisher()));
        var entera = await resolver.ResolverAsync(PorFiltro(body, excluidos), Contexto(), ct);
        var cortada = await resolver.ResolverAsync(PorFiltro(body, excluidos), Contexto(), 4, ct);
        var total = await resolver.ContarAsync(PorFiltro(body, excluidos), Contexto(), ct);
        var totalIds = await resolver.ContarAsync(new SeleccionPorIds([ids[1], ids[2], Guid.NewGuid()]), Contexto(), ct);
        var sinOrganismo = await resolver.ContarAsync(PorFiltro(body), Contexto(HierarchyScenario.X), ct);

        entera.Should().HaveCount(7, "9 de C1 menos 2 excluidos; los borrados de C2 no están en la bandeja");
        cortada.Select(r => r.Id).Should().Equal(entera.Take(cortada.Count).Select(r => r.Id), "un prefijo en el mismo orden");
        cortada.Count.Should().BeGreaterThanOrEqualTo(4, "con 4 o más en la selección devuelve al menos el límite");
        total.Should().Be(entera.Count);
        totalIds.Should().Be(2);
        sinOrganismo.Should().Be(0);
    }
}
