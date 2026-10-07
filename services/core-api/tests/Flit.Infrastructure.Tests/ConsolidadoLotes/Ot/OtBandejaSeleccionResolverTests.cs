using Flit.Admin.Domain.OtClientProcedures;
using Flit.Api.Endpoints;
using Flit.Infrastructure.ConsolidadoLotes.Ot;
using Flit.Queries.Domain;
using Flit.Tramites.Application.UseCases.ConsolidadoLotes;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities.ConsolidadoLotes;
using Flit.Tramites.Domain.Repositories;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Infrastructure.Tests.ConsolidadoLotes.Ot;

/// <summary>
/// HU #13390 (Épica #13216) — <see cref="OtBandejaSeleccionResolver"/>: la selección del lote desde la bandeja
/// del OT (origen <c>ot_bandeja</c>). Sin PostgreSQL: el repositorio OT es un sustituto. El universo de la
/// bandeja (organismo, recibido, vivo, sin grant) es de <c>ListAccessibleRefsAsync</c> y se prueba contra
/// PostgreSQL en <c>OtBandejaSeleccionResolverIntegrationTests</c>; aquí se fija que el resolver la usa tal
/// cual (filtro, ids, organismo del contexto), resta los excluidos, valida el filtro y pone como compañía
/// del ítem la del trámite.
/// <para>Uso de ejemplo:
/// <code>
/// var refs = await porOrigen.Para(ConsolidadoExportOrigin.OtBandeja).ResolverAsync(
///     new SeleccionPorFiltro(new OtBandejaLoteFiltro(body.ToFilter()), excluidos),
///     new LoteSeleccionContexto(tenantOt, usuario, transitOfficeIdDelSuperAdmin), ct);
/// </code></para>
/// </summary>
public sealed class OtBandejaSeleccionResolverTests
{
    private static readonly Guid TenantOt = Guid.Parse("e1000000-0000-4000-8000-0000000000a1");
    private static readonly Guid Usuario = Guid.Parse("e1000000-0000-4000-8000-0000000000a2");
    private static readonly Guid ClienteA = Guid.Parse("e1000000-0000-4000-8000-0000000000b1");
    private static readonly Guid ClienteB = Guid.Parse("e1000000-0000-4000-8000-0000000000b2");
    private static readonly Guid Organismo = Guid.Parse("e1000000-0000-4000-8000-0000000000e5");

    private readonly IOtClientProcedureRepository _repo = Substitute.For<IOtClientProcedureRepository>();

    private OtBandejaSeleccionResolver Sut() => new(_repo);

    private static LoteSeleccionContexto Contexto(Guid? organismo = null) => new(TenantOt, Usuario, organismo);

    private static OtClientProcedureRef Ref(Guid cliente, int n) =>
        new(Guid.Parse($"e1000000-0000-4000-8000-{n:D12}"), cliente, $"{n:D6}", $"PL{n:D4}");

    private void RepoDevuelve(params OtClientProcedureRef[] refs) =>
        _repo.ListAccessibleRefsAsync(
                Arg.Any<Guid>(), Arg.Any<OtClientProcedureFilter?>(), Arg.Any<IReadOnlyCollection<Guid>?>(),
                Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(refs);

    [Fact]
    public void Origen_es_ot_bandeja_del_DDL()
    {
        Sut().Origen.Should().Be(ConsolidadoExportOrigin.OtBandeja);
    }

    [Fact]
    public async Task AC1_filtro_pasa_los_criterios_de_la_bandeja_y_conserva_su_orden()
    {
        var refs = new[] { Ref(ClienteB, 3), Ref(ClienteA, 1), Ref(ClienteA, 2) };
        RepoDevuelve(refs);
        var criterios = new OtClientProcedureFilter
        {
            Condiciones = [new QueryCondition("placa", QueryOperator.EsAlguno, ["PL0001", "PL0002", "PL0003"])],
            SortBy = "placa",
            SortDir = "desc",
        };

        var result = await Sut().ResolverAsync(
            new SeleccionPorFiltro(new OtBandejaLoteFiltro(criterios)), Contexto(), TestContext.Current.CancellationToken);

        result.Select(r => r.Id).Should().Equal(refs.Select(r => r.Id));
        await _repo.Received(1).ListAccessibleRefsAsync(
            TenantOt, criterios, null, null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AC2_filtro_resta_los_excluidos_sin_tope_y_en_orden()
    {
        var refs = Enumerable.Range(1, 250).Select(n => Ref(ClienteA, n)).ToArray();
        RepoDevuelve(refs);
        var excluidos = new[] { refs[0].Id, refs[100].Id, refs[249].Id, Guid.NewGuid() };

        var result = await Sut().ResolverAsync(
            new SeleccionPorFiltro(new OtBandejaLoteFiltro(new OtClientProcedureFilter { PageSize = 20 }), excluidos),
            Contexto(), TestContext.Current.CancellationToken);

        result.Should().HaveCount(247);
        result.Select(r => r.Id).Should().Equal(refs.Select(r => r.Id).Except(excluidos));
    }

    [Fact]
    public async Task Tope_de_excluidos_no_lo_aplica_el_resolver_sino_la_creacion()
    {
        RepoDevuelve(Ref(ClienteA, 1));
        var excluidos = Enumerable.Range(0, LoteSeleccionTopes.MaxExcluidos + 1).Select(_ => Guid.NewGuid()).ToList();

        var act = () => Sut().ResolverAsync(
            new SeleccionPorFiltro(new OtBandejaLoteFiltro(new OtClientProcedureFilter()), excluidos),
            Contexto(), TestContext.Current.CancellationToken);

        (await act.Should().NotThrowAsync()).Which.Should().ContainSingle();
    }

    [Fact]
    public async Task AC3_ids_van_sin_filtro_y_deduplicados_y_solo_vuelve_lo_de_la_bandeja()
    {
        var dentro = Ref(ClienteA, 1);
        RepoDevuelve(dentro);
        var fuera = Guid.NewGuid();

        var result = await Sut().ResolverAsync(
            new SeleccionPorIds([dentro.Id, fuera, dentro.Id]), Contexto(), TestContext.Current.CancellationToken);

        result.Should().ContainSingle().Which.Id.Should().Be(dentro.Id);
        await _repo.Received(1).ListAccessibleRefsAsync(
            TenantOt,
            null,
            Arg.Is<IReadOnlyCollection<Guid>?>(ids => ids != null && ids.Count == 2 && ids.Contains(dentro.Id) && ids.Contains(fuera)),
            null,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Ids_vacios_no_consultan()
    {
        var result = await Sut().ResolverAsync(new SeleccionPorIds([]), Contexto(), TestContext.Current.CancellationToken);

        result.Should().BeEmpty();
        await _repo.DidNotReceiveWithAnyArgs().ListAccessibleRefsAsync(default, default, default, default, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Organismo_del_super_admin_viaja_al_repositorio()
    {
        RepoDevuelve();

        await Sut().ResolverAsync(new SeleccionPorIds([Guid.NewGuid()]), Contexto(Organismo), TestContext.Current.CancellationToken);
        await Sut().ResolverAsync(
            new SeleccionPorFiltro(new OtBandejaLoteFiltro(new OtClientProcedureFilter())), Contexto(Organismo),
            TestContext.Current.CancellationToken);

        await _repo.Received(2).ListAccessibleRefsAsync(
            TenantOt, Arg.Any<OtClientProcedureFilter?>(), Arg.Any<IReadOnlyCollection<Guid>?>(), Organismo,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AC5_sin_organismo_resoluble_el_resultado_es_vacio()
    {
        RepoDevuelve();

        var porFiltro = await Sut().ResolverAsync(
            new SeleccionPorFiltro(new OtBandejaLoteFiltro(new OtClientProcedureFilter())), Contexto(),
            TestContext.Current.CancellationToken);
        var porIds = await Sut().ResolverAsync(
            new SeleccionPorIds([Guid.NewGuid()]), Contexto(), TestContext.Current.CancellationToken);

        porFiltro.Should().BeEmpty();
        porIds.Should().BeEmpty();
    }

    [Fact]
    public async Task AC6_la_compania_del_item_es_la_del_tramite_no_el_tenant_OT()
    {
        var a = Ref(ClienteA, 7);
        var b = Ref(ClienteB, 8) with { Plate = null };
        RepoDevuelve(a, b);

        var result = await Sut().ResolverAsync(
            new SeleccionPorFiltro(new OtBandejaLoteFiltro(new OtClientProcedureFilter())), Contexto(),
            TestContext.Current.CancellationToken);

        result.Should().Equal(
            new ProcedureInstanceRef(a.Id, ClienteA, a.ReferenceNumber, a.Plate),
            new ProcedureInstanceRef(b.Id, ClienteB, b.ReferenceNumber, null));
        result.Should().NotContain(r => r.TenantId == TenantOt);
    }

    [Fact]
    public async Task Condicion_fuera_del_catalogo_de_la_bandeja_es_filtro_invalido()
    {
        var criterios = new OtClientProcedureFilter
        {
            Condiciones = [new QueryCondition("campo_inexistente", QueryOperator.EsAlguno, ["x"])],
        };

        var act = () => Sut().ResolverAsync(
            new SeleccionPorFiltro(new OtBandejaLoteFiltro(criterios)), Contexto(), TestContext.Current.CancellationToken);

        (await act.Should().ThrowAsync<LoteSeleccionInvalidaException>())
            .Which.Codigo.Should().Be(LoteSeleccionInvalidaException.CodigoFiltroInvalido);
        await _repo.DidNotReceiveWithAnyArgs().ListAccessibleRefsAsync(default, default, default, default, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Familia_fuera_del_dominio_es_filtro_invalido()
    {
        var act = () => Sut().ResolverAsync(
            new SeleccionPorFiltro(new OtBandejaLoteFiltro(new OtClientProcedureFilter { Familia = "NO_EXISTE" })),
            Contexto(), TestContext.Current.CancellationToken);

        (await act.Should().ThrowAsync<LoteSeleccionInvalidaException>())
            .Which.Codigo.Should().Be(LoteSeleccionInvalidaException.CodigoFiltroInvalido);
    }

    [Fact]
    public async Task Filtro_de_otro_origen_se_rechaza()
    {
        var act = () => Sut().ResolverAsync(
            new SeleccionPorFiltro(new TramitesLoteFiltro(new ProcedureInstanceListRequest())), Contexto(),
            TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task Sin_tenant_OT_en_el_contexto_falla_cerrado()
    {
        var act = () => Sut().ResolverAsync(
            new SeleccionPorIds([Guid.NewGuid()]), new LoteSeleccionContexto(null, Usuario),
            TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<ArgumentException>();
        await _repo.DidNotReceiveWithAnyArgs().ListAccessibleRefsAsync(default, default, default, default, TestContext.Current.CancellationToken);
    }

    [Fact]
    public void Por_origen_elige_ot_bandeja_junto_al_resolver_de_tramites()
    {
        var tramites = Substitute.For<ILoteSeleccionResolver>();
        tramites.Origen.Returns(ConsolidadoExportOrigin.Tramites);
        var ot = Sut();

        var porOrigen = new LoteSeleccionResolverPorOrigen([tramites, ot]);

        porOrigen.Para(ConsolidadoExportOrigin.OtBandeja).Should().BeSameAs(ot);
        porOrigen.Para(ConsolidadoExportOrigin.Tramites).Should().BeSameAs(tramites);
    }

    [Fact]
    public void ToFilter_de_la_busqueda_copia_todos_los_criterios_menos_la_pagina()
    {
        var body = new OtBandejaSearchRequest
        {
            Condiciones = [new QueryCondition("placa", QueryOperator.EsAlguno, ["ABC123"])],
            Busqueda = "texto",
            Status = "entregado",
            HasActiveRevocationRequest = true,
            ProcedureTypeId = Guid.NewGuid(),
            Familia = "TRASPASO",
            Vin = "vin",
            Placa = "pla",
            Vendedor = "ven",
            Comprador = "com",
            Gestor = "ges",
            CreatedFrom = DateTimeOffset.UtcNow.AddDays(-3),
            CreatedTo = DateTimeOffset.UtcNow.AddDays(-2),
            UpdatedFrom = DateTimeOffset.UtcNow.AddDays(-1),
            UpdatedTo = DateTimeOffset.UtcNow,
            SortBy = "placa",
            SortDir = "asc",
            Page = 7,
            PageSize = 13,
        };

        var filter = body.ToFilter();

        // Cada propiedad del filtro que existe en el cuerpo se copia; Page/PageSize se quedan en su valor por
        // defecto. Por reflexión: un criterio nuevo en la bandeja que no llegue a la selección rompe aquí.
        var defecto = new OtClientProcedureFilter();
        foreach (var prop in typeof(OtClientProcedureFilter).GetProperties())
        {
            if (prop.Name is nameof(OtClientProcedureFilter.Page) or nameof(OtClientProcedureFilter.PageSize))
            {
                prop.GetValue(filter).Should().Be(prop.GetValue(defecto), prop.Name);
                continue;
            }

            var origen = typeof(OtBandejaSearchRequest).GetProperty(prop.Name);
            origen.Should().NotBeNull($"el cuerpo de la búsqueda debe traer {prop.Name}");
            prop.GetValue(filter).Should().Be(origen!.GetValue(body), prop.Name);
        }
    }
}
