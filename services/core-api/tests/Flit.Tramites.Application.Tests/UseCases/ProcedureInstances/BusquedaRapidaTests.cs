using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Estados;
using Flit.Tramites.Domain.Tramites.ValueObjects;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

/// <summary>
/// Epic #12686 — HU #12805. Atajos de la búsqueda rápida que necesitan algo nuevo: días en Entregado
/// (SQL), y sin firmas / sin documento / pausados (evaluados en memoria sobre los borradores con las
/// MISMAS reglas que la etiqueta del actor y el gate de radicación).
///
/// <para>Uso de ejemplo:
/// <c>var filtro = await Resolver().AplicarAsync(new(), BusquedaRapida.SinFirmas, Cargar(a, b), ct);</c>
/// ⇒ <c>filtro.IdsIncluidos</c> = los borradores sin firmar.</para>
/// </summary>
public sealed class BusquedaRapidaTests
{
    private const string DocFactura = "factura";

    private readonly IProcedureInstanceRepository _repo = Substitute.For<IProcedureInstanceRepository>();
    private readonly IChecklistCompanyParamsProvider _params = Substitute.For<IChecklistCompanyParamsProvider>();
    private readonly IResolvedChecklistMatrixProvider _matriz = Substitute.For<IResolvedChecklistMatrixProvider>();

    public BusquedaRapidaTests()
    {
        _repo.GetTenantNamesAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, string>());
        _repo.GetUserDisplayNamesAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, string>());
        _repo.ListFirmaBaulVigenciaKeysAsync(
                Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<string, bool>());
        _repo.ListVigenteApprovedIdentityKeysAsync(
                Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(_aprobadas);
        _params.GetForTenantAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<CompanyDocumentParam>());
        _matriz.GetForAsync(Arg.Any<Guid>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns([new ResolvedChecklistDoc(DocFactura, "Factura", Obligatorio: true, Orden: 1)]);
    }

    // ── Validación del parámetro ──────────────────────────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("sin_firmas")]
    [InlineData(" PAUSADOS ")]
    [InlineData("mas_de_10_dias")]
    public void AtajoValidoOAusente_NoEsError(string? atajo) =>
        BusquedaRapida.Validate(atajo).Should().BeNull();

    [Fact] // AC7 — un atajo desconocido se rechaza nombrándolo, no se ignora.
    public void AtajoDesconocido_DevuelveMensajeQueLoNombra() =>
        BusquedaRapida.Validate("mis_tramites").Should().Contain("mis_tramites").And.Contain("sin_firmas");

    // ── AC1 — días en Entregado ───────────────────────────────────────────────────────────

    [Fact]
    public void CorteDeDias_EsElInicioDelDiaEnColombia()
    {
        // 22 sep 2026 a las 03:00 UTC es todavía el 21 en Colombia (UTC-5).
        var ahora = new DateTimeOffset(2026, 9, 22, 3, 0, 0, TimeSpan.Zero);

        var corte = BusquedaRapida.CorteDeDias(5, ahora);

        corte.Should().Be(new DateTimeOffset(2026, 9, 16, 0, 0, 0, TimeSpan.FromHours(-5)));
        corte.Offset.Should().Be(TimeSpan.Zero, "Npgsql solo escribe timestamptz en UTC");
    }

    [Fact]
    public void CorteDeDias_CuentaDiasCalendario_SeisDiasPasaElCorteDeCincoYNoElDeDiez()
    {
        var ahora = new DateTimeOffset(2026, 9, 7, 15, 0, 0, TimeSpan.FromHours(-5));
        var entroElDia1 = new DateTimeOffset(2026, 9, 1, 18, 0, 0, TimeSpan.FromHours(-5));
        var entroElDia2 = new DateTimeOffset(2026, 9, 2, 8, 0, 0, TimeSpan.FromHours(-5));

        (entroElDia1 < BusquedaRapida.CorteDeDias(5, ahora)).Should().BeTrue("del 1 al 7 van 6 días");
        (entroElDia2 < BusquedaRapida.CorteDeDias(5, ahora)).Should().BeFalse("del 2 al 7 van 5 días, no más de 5");
        (entroElDia1 < BusquedaRapida.CorteDeDias(10, ahora)).Should().BeFalse();
    }

    [Fact]
    public async Task MasDe5Dias_SeResuelveEnSqlSinCargarCandidatos()
    {
        var cargado = false;

        var filtro = await Resolver().AplicarAsync(
            new ProcedureInstanceListFilter(), BusquedaRapida.MasDe5Dias,
            (_, _, _) => { cargado = true; return Task.FromResult<(IReadOnlyList<ProcedureInstance>, int)>(([], 0)); },
            TestContext.Current.CancellationToken);

        filtro.EntregadoAntesDe.Should().NotBeNull();
        filtro.IdsIncluidos.Should().BeNull();
        cargado.Should().BeFalse();
    }

    // ── AC2 — sin firmas ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SinFirmas_TraeSoloLosBorradoresConUnaParteSinFirmar()
    {
        var sinFirmar = Borrador();
        var firmado = Borrador();
        IdentidadAprobada(firmado);
        ProcedureInstanceListFilter? pedido = null;

        var filtro = await Resolver().AplicarAsync(
            new ProcedureInstanceListFilter { Placa = "ABC123" }, BusquedaRapida.SinFirmas,
            (f, _, _) => { pedido = f; return Task.FromResult<(IReadOnlyList<ProcedureInstance>, int)>(([sinFirmar, firmado], 2)); },
            TestContext.Current.CancellationToken);

        filtro.IdsIncluidos.Should().BeEquivalentTo([sinFirmar.Id]);
        pedido!.Estados.Should().Equal(TramiteEstado.Borrador);
        pedido.Placa.Should().Be("ABC123", "los candidatos respetan el resto de filtros");
    }

    [Fact]
    public async Task SinFirmas_CoincideConLaEtiquetaDelActorDelListado()
    {
        var sinFirmar = Borrador();
        var firmado = Borrador();
        IdentidadAprobada(firmado);
        var ct = TestContext.Current.CancellationToken;

        // La etiqueta, tal como la pinta el listado (ruta pública del handler, sin atajo).
        _repo.ListWithSummaryGraphFilteredAsync(
                Arg.Any<Guid?>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<ProcedureInstanceListFilter>(),
                Arg.Any<ProcedureInstanceSortBy>(), Arg.Any<SortDirection>(), Arg.Any<CancellationToken>())
            .Returns(((IReadOnlyList<ProcedureInstance>)[sinFirmar, firmado], 2));
        var (filas, _) = await new ListProcedureInstancesFilteredHandler(_repo)
            .HandleAsync(new ProcedureInstanceListRequest(), ct);
        var filtro = await Resolver().AplicarAsync(
            new ProcedureInstanceListFilter(), BusquedaRapida.SinFirmas, Cargar(sinFirmar, firmado), ct);

        var etiquetadasSinFirmar = filas
            .Where(f => f.FirmaCompradorEstado is not null && f.FirmaCompradorEstado != FirmaParteEstados.Firmado)
            .Select(f => f.Id);
        filtro.IdsIncluidos.Should().BeEquivalentTo(etiquetadasSinFirmar);
    }

    // ── AC3 — sin documento ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task SinDocumento_TraeLosBorradoresAQuienesLesFaltaUnObligatorio()
    {
        var incompleto = Borrador();
        var completo = Borrador(conFactura: true);

        var filtro = await Resolver().AplicarAsync(
            new ProcedureInstanceListFilter(), BusquedaRapida.SinDocumento, Cargar(incompleto, completo),
            TestContext.Current.CancellationToken);

        filtro.IdsIncluidos.Should().BeEquivalentTo([incompleto.Id]);
    }

    [Fact]
    public async Task SinDocumento_NoCuentaLosDocumentosDeEjemploNiLosGeneradosPorFlit()
    {
        _matriz.GetForAsync(Arg.Any<Guid>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns([
                new ResolvedChecklistDoc(DocFactura, "Factura", Obligatorio: true, Orden: 1),
                new ResolvedChecklistDoc("fur", "FUR", Obligatorio: true, Orden: 2, EsGeneradoSistema: true),
            ]);
        var completo = Borrador(conFactura: true);

        var filtro = await Resolver().AplicarAsync(
            new ProcedureInstanceListFilter(), BusquedaRapida.SinDocumento, Cargar(completo),
            TestContext.Current.CancellationToken);

        filtro.IdsIncluidos.Should().BeEmpty("el FUR lo genera FLIT: no se pide cargar");
    }

    [Fact]
    public async Task SinDocumento_LeeLaMatrizUnaVezPorTipoYOrganismo()
    {
        await Resolver().AplicarAsync(
            new ProcedureInstanceListFilter(), BusquedaRapida.SinDocumento,
            Cargar(Borrador(), Borrador(), Borrador()), TestContext.Current.CancellationToken);

        await _matriz.Received(1).GetForAsync(Arg.Any<Guid>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>());
        await _params.Received(1).GetForTenantAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    // ── AC4 — pausados ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Pausados_EsSinFirmasOSinDocumentoOPausadoManualmente()
    {
        var sinFirmar = Borrador(conFactura: true);
        var sinDocumento = Borrador();
        IdentidadAprobada(sinDocumento);
        var pausadoListo = Borrador(conFactura: true);
        IdentidadAprobada(pausadoListo);
        pausadoListo.IsPaused = true;
        var listo = Borrador(conFactura: true);
        IdentidadAprobada(listo);

        var filtro = await Resolver().AplicarAsync(
            new ProcedureInstanceListFilter(), BusquedaRapida.Pausados,
            Cargar(sinFirmar, sinDocumento, pausadoListo, listo), TestContext.Current.CancellationToken);

        filtro.IdsIncluidos.Should().BeEquivalentTo([sinFirmar.Id, sinDocumento.Id, pausadoListo.Id]);
    }

    // ── AC6 — tope ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task MasBorradoresQueElTope_LanzaEnVezDeTruncar()
    {
        var act = () => Resolver().AplicarAsync(
            new ProcedureInstanceListFilter(), BusquedaRapida.SinFirmas,
            (_, take, _) => Task.FromResult<(IReadOnlyList<ProcedureInstance>, int)>(([], take + 1)),
            TestContext.Current.CancellationToken);

        (await act.Should().ThrowAsync<BusquedaRapidaDemasiadoAmpliaException>())
            .Which.Message.Should().Contain("Acota la búsqueda");
    }

    // ── AC5 — el handler pagina sobre el resultado del atajo ──────────────────────────────

    [Fact]
    public async Task ElListadoPaginaSobreLosIdsDelAtajo()
    {
        var sinFirmar = Borrador();
        var ct = TestContext.Current.CancellationToken;
        _repo.ListWithSummaryGraphFilteredAsync(
                Arg.Any<Guid?>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<ProcedureInstanceListFilter>(),
                Arg.Any<ProcedureInstanceSortBy>(), Arg.Any<SortDirection>(), Arg.Any<CancellationToken>())
            .Returns(((IReadOnlyList<ProcedureInstance>)[sinFirmar], 1));

        await new ListProcedureInstancesFilteredHandler(_repo, Resolver()).HandleAsync(
            new ProcedureInstanceListRequest { BusquedaRapida = BusquedaRapida.SinFirmas, Skip = 20, Take = 20 }, ct);

        await _repo.Received(1).ListWithSummaryGraphFilteredAsync(
            Arg.Any<Guid?>(), 20, 20,
            Arg.Is<ProcedureInstanceListFilter>(f => f.IdsIncluidos != null && f.IdsIncluidos.Contains(sinFirmar.Id)),
            Arg.Any<ProcedureInstanceSortBy>(), Arg.Any<SortDirection>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task LaTiraCuentaSobreLosIdsDelAtajo()
    {
        var sinFirmar = Borrador();
        _repo.ListWithSummaryGraphFilteredAsync(
                Arg.Any<Guid?>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<ProcedureInstanceListFilter>(),
                Arg.Any<ProcedureInstanceSortBy>(), Arg.Any<SortDirection>(), Arg.Any<CancellationToken>())
            .Returns(((IReadOnlyList<ProcedureInstance>)[sinFirmar], 1));
        _repo.CountByStatusFilteredAsync(Arg.Any<Guid?>(), Arg.Any<ProcedureInstanceListFilter>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<string, int> { [TramiteEstado.Borrador] = 1 });

        var conteos = await new CountProcedureInstancesByStatusHandler(_repo, Resolver()).HandleAsync(
            new ProcedureInstanceListRequest { BusquedaRapida = BusquedaRapida.SinFirmas },
            TestContext.Current.CancellationToken);

        conteos[TramiteEstado.Borrador].Should().Be(1);
        await _repo.Received(1).CountByStatusFilteredAsync(
            Arg.Any<Guid?>(),
            Arg.Is<ProcedureInstanceListFilter>(f => f.IdsIncluidos != null && f.IdsIncluidos.SequenceEqual(new[] { sinFirmar.Id })),
            Arg.Any<CancellationToken>());
    }

    // ── Helpers ───────────────────────────────────────────────────────────────────────────

    private BusquedaRapidaResolver Resolver() => new(_repo, _params, _matriz);

    private static BusquedaRapidaResolver.CargarCandidatos Cargar(params ProcedureInstance[] candidatos) =>
        (_, _, _) => Task.FromResult<(IReadOnlyList<ProcedureInstance>, int)>((candidatos, candidatos.Length));

    /// <summary>Deja la identidad del comprador aprobada y vigente, como la vería el listado.</summary>
    private void IdentidadAprobada(ProcedureInstance instance)
    {
        var actor = instance.Actors.First();
        _aprobadas.Add(BiometricRules.IdentidadKey(instance.TenantId, actor.DocumentType, actor.DocumentNumber));
    }

    private readonly HashSet<string> _aprobadas = new(StringComparer.Ordinal);

    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Organismo = Guid.NewGuid();

    /// <summary>Matrícula inicial en borrador con un comprador persona natural.</summary>
    private static ProcedureInstance Borrador(bool conFactura = false)
    {
        var tipo = ProcedureTypeFixture.Matricula;
        var instance = new ProcedureInstance
        {
            ProcedureType = tipo,
            ProcedureTypeId = tipo.Id,
            Id = Guid.NewGuid(),
            TenantId = Tenant,
            TransitOfficeId = Organismo,
            ReferenceNumber = "FT1-0000001",
            Status = TramiteEstado.Borrador,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        instance.Actors.Add(new ProcedureInstanceActor
        {
            ActorType = "comprador",
            DocumentType = "CC",
            DocumentNumber = Random.Shared.Next(10_000_000, 99_999_999).ToString(System.Globalization.CultureInfo.InvariantCulture),
            FullName = "Ana Compradora",
        });
        if (conFactura)
        {
            instance.Attachments.Add(new ProcedureInstanceAttachment
            {
                Id = Guid.NewGuid(),
                Tipo = DocFactura,
                Filename = "factura.pdf",
            });
        }

        return instance;
    }
}
