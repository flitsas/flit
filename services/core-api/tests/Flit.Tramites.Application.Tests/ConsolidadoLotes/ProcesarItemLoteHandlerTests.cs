using Flit.Tramites.Application.UseCases.ConsolidadoLotes;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Entities.ConsolidadoLotes;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Estados;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Flit.Tramites.Application.Tests.ConsolidadoLotes;

/// <summary>
/// HU #13375 — <see cref="ProcesarItemLoteHandler"/>: revalidación de acceso → entregador → incluido, omitido con
/// texto legible o reintento. El acceso, el entregador y la persistencia son sustitutos; los orígenes
/// (<see cref="TramitesLoteItemOrigen"/>, <see cref="SuperAdminLoteItemOrigen"/>) son los reales. El AC5 usa el
/// entregador REAL sobre un trámite que ya tiene el consolidado persistido.
/// <para>Uso de ejemplo:
/// <c>var r = await handler.HandleAsync(ProcesarItemLoteCommand.Con(lote, item, settings), ct);</c> ⇒
/// <c>r.Desenlace</c> (<c>Incluido</c> | <c>Omitido</c> | <c>Reprogramado</c>) y <c>r.Aplicado</c>.</para>
/// </summary>
public sealed class ProcesarItemLoteHandlerTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 10, 7, 15, 0, 0, TimeSpan.Zero);

    private readonly IConsolidadoLoteAccessChecker _acceso = Substitute.For<IConsolidadoLoteAccessChecker>();
    private readonly ILoteItemEntregador _entregador = Substitute.For<ILoteItemEntregador>();
    private readonly IConsolidadoLoteItemProceso _proceso = Substitute.For<IConsolidadoLoteItemProceso>();

    public ProcesarItemLoteHandlerTests()
    {
        _acceso.TieneAccesoAsync(Arg.Any<LoteItemContexto>(), Arg.Any<CancellationToken>()).Returns(true);
        _proceso.MarcarIncluidoAsync(Arg.Any<LoteItemIncluido>(), Arg.Any<CancellationToken>()).Returns(true);
        _proceso.MarcarOmitidoAsync(Arg.Any<LoteItemOmitido>(), Arg.Any<CancellationToken>()).Returns(true);
        _proceso.ReprogramarAsync(Arg.Any<LoteItemReintento>(), Arg.Any<CancellationToken>()).Returns(true);
    }

    private ProcesarItemLoteHandler Handler(ILoteItemEntregador? entregador = null)
    {
        var e = entregador ?? _entregador;
        return new ProcesarItemLoteHandler(
            new LoteItemOrigenPorOrigen([new TramitesLoteItemOrigen(_acceso, e), new SuperAdminLoteItemOrigen(_acceso, e)]),
            _proceso,
            NullLogger<ProcesarItemLoteHandler>.Instance,
            new RelojFijo(Ahora));
    }

    private static readonly LoteItemAdjunto Adjunto =
        new(Guid.NewGuid(), "fm/consolidado-13375", 4096, "sha-13375", "consolidado.pdf");

    // ── AC1 — ítem incluido ─────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(LoteDeliveryMode.Existente)]
    [InlineData(LoteDeliveryMode.Generado)]
    public async Task AC1_EntregadorIncluye_MarcaIncluidoConSnapshotYModo(string modo)
    {
        var ct = TestContext.Current.CancellationToken;
        var (lote, item) = Lote();
        _entregador.EntregarAsync(Arg.Any<LoteItemEntregaRequest>(), Arg.Any<CancellationToken>())
            .Returns(LoteItemEntregaResult.Incluido(Adjunto, modo));

        var r = await Handler().HandleAsync(Cmd(lote, item), ct);

        r.Desenlace.Should().Be(ProcesarItemLoteDesenlace.Incluido);
        r.Aplicado.Should().BeTrue();
        r.DeliveryMode.Should().Be(modo);
        await _proceso.Received(1).MarcarIncluidoAsync(
            new LoteItemIncluido(lote.Id, item.Id, Adjunto, modo, Ahora), Arg.Any<CancellationToken>());
        await _proceso.DidNotReceive().MarcarOmitidoAsync(Arg.Any<LoteItemOmitido>(), Arg.Any<CancellationToken>());
        await _proceso.DidNotReceive().ReprogramarAsync(Arg.Any<LoteItemReintento>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AC1_Contrato_ElEntregadorRecibeTramiteCompaniaYTipoDelLote()
    {
        var ct = TestContext.Current.CancellationToken;
        var (lote, item) = Lote(tipo: ConsolidadoExportDocumentType.ConsolidadoMaestro);
        _entregador.EntregarAsync(Arg.Any<LoteItemEntregaRequest>(), Arg.Any<CancellationToken>())
            .Returns(LoteItemEntregaResult.Incluido(Adjunto, LoteDeliveryMode.Existente));

        await Handler().HandleAsync(Cmd(lote, item), ct);

        await _entregador.Received(1).EntregarAsync(
            Arg.Is<LoteItemEntregaRequest>(q => q.ProcedureInstanceId == item.ProcedureInstanceId
                && q.TenantId == item.TenantId
                && q.TipoDocumento == LoteTipoDocumento.ConsolidadoMaestro
                && q.PrecedenciaMatriz == null && q.AntesDeGenerar == null),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AC1_Edge_ElCierreNoAplica_SiElItemYaNoEstaProcesando()
    {
        var ct = TestContext.Current.CancellationToken;
        var (lote, item) = Lote();
        _entregador.EntregarAsync(Arg.Any<LoteItemEntregaRequest>(), Arg.Any<CancellationToken>())
            .Returns(LoteItemEntregaResult.Incluido(Adjunto, LoteDeliveryMode.Existente));
        _proceso.MarcarIncluidoAsync(Arg.Any<LoteItemIncluido>(), Arg.Any<CancellationToken>()).Returns(false);

        var r = await Handler().HandleAsync(Cmd(lote, item), ct);

        r.Desenlace.Should().Be(ProcesarItemLoteDesenlace.Incluido);
        r.Aplicado.Should().BeFalse("lote cancelado o ítem ya cerrado: el cierre en vuelo no sobrescribe");
    }

    [Fact]
    public async Task AC1_Edge_IncluidoSinAdjunto_SeTrataComoFalloTecnico()
    {
        var ct = TestContext.Current.CancellationToken;
        var (lote, item) = Lote();
        _entregador.EntregarAsync(Arg.Any<LoteItemEntregaRequest>(), Arg.Any<CancellationToken>())
            .Returns(new LoteItemEntregaResult(LoteItemEntregaEstado.Incluido));

        var r = await Handler().HandleAsync(Cmd(lote, item), ct);

        r.Desenlace.Should().Be(ProcesarItemLoteDesenlace.Reprogramado);
        await _proceso.DidNotReceive().MarcarIncluidoAsync(Arg.Any<LoteItemIncluido>(), Arg.Any<CancellationToken>());
    }

    // ── AC2 — acceso revocado ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AC2_AccesoRevocado_OmiteConAccesoRevocado_YNoLlamaAlEntregador()
    {
        var ct = TestContext.Current.CancellationToken;
        var (lote, item) = Lote();
        _acceso.TieneAccesoAsync(Arg.Any<LoteItemContexto>(), Arg.Any<CancellationToken>()).Returns(false);

        var r = await Handler().HandleAsync(Cmd(lote, item), ct);

        r.Desenlace.Should().Be(ProcesarItemLoteDesenlace.Omitido);
        r.Codigo.Should().Be(ConsolidadoLoteOmisiones.AccesoRevocado);
        r.Motivo.Should().Be("Acceso revocado");
        await _entregador.DidNotReceive().EntregarAsync(Arg.Any<LoteItemEntregaRequest>(), Arg.Any<CancellationToken>());
        await _proceso.Received(1).MarcarOmitidoAsync(
            new LoteItemOmitido(lote.Id, item.Id, ConsolidadoLoteOmisiones.AccesoRevocado, "Acceso revocado", 0, Ahora),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AC2_Edge_LaRevalidacionFalla_SeReintentaComoErrorTecnico_SinEntregar()
    {
        var ct = TestContext.Current.CancellationToken;
        var (lote, item) = Lote();
        _acceso.TieneAccesoAsync(Arg.Any<LoteItemContexto>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new TimeoutException("bd lenta (simulado)"));

        var r = await Handler().HandleAsync(Cmd(lote, item), ct);

        r.Desenlace.Should().Be(ProcesarItemLoteDesenlace.Reprogramado);
        await _entregador.DidNotReceive().EntregarAsync(Arg.Any<LoteItemEntregaRequest>(), Arg.Any<CancellationToken>());
    }

    // ── AC3 — error de precondición ─────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(ConsolidadoLoteOmisiones.SinAdjuntos)]
    [InlineData(ConsolidadoLoteOmisiones.FurRequerido)]
    [InlineData(ConsolidadoLoteOmisiones.MigradoSoloLectura)]
    [InlineData(ConsolidadoLoteOmisiones.OrganismoRequerido)]
    public async Task AC3_Precondicion_OmiteConElTextoDelCatalogo(string codigo)
    {
        var ct = TestContext.Current.CancellationToken;
        var (lote, item) = Lote();
        _entregador.EntregarAsync(Arg.Any<LoteItemEntregaRequest>(), Arg.Any<CancellationToken>())
            .Returns(LoteItemEntregaResult.Omitido(codigo));

        var r = await Handler().HandleAsync(Cmd(lote, item), ct);

        r.Desenlace.Should().Be(ProcesarItemLoteDesenlace.Omitido);
        r.Codigo.Should().Be(codigo);
        r.Motivo.Should().Be(ConsolidadoErrorTextos.ParaLote(codigo));
        await _proceso.Received(1).MarcarOmitidoAsync(
            Arg.Is<LoteItemOmitido>(o => o.Codigo == codigo && o.Motivo == ConsolidadoErrorTextos.ParaLote(codigo)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AC3_ElLoteContinua_ConElSiguienteItem()
    {
        var ct = TestContext.Current.CancellationToken;
        var (lote, malo) = Lote();
        var bueno = Item(lote, Guid.NewGuid());
        _entregador.EntregarAsync(Arg.Is<LoteItemEntregaRequest>(q => q.ProcedureInstanceId == malo.ProcedureInstanceId), Arg.Any<CancellationToken>())
            .Returns(LoteItemEntregaResult.Omitido(ConsolidadoLoteOmisiones.SinAdjuntos));
        _entregador.EntregarAsync(Arg.Is<LoteItemEntregaRequest>(q => q.ProcedureInstanceId == bueno.ProcedureInstanceId), Arg.Any<CancellationToken>())
            .Returns(LoteItemEntregaResult.Incluido(Adjunto, LoteDeliveryMode.Existente));
        var handler = Handler();

        var r1 = await handler.HandleAsync(Cmd(lote, malo), ct);
        var r2 = await handler.HandleAsync(Cmd(lote, bueno), ct);

        r1.Desenlace.Should().Be(ProcesarItemLoteDesenlace.Omitido);
        r2.Desenlace.Should().Be(ProcesarItemLoteDesenlace.Incluido);
    }

    // ── AC4 — error técnico con reintento ───────────────────────────────────────────────────────

    [Fact]
    public async Task AC4_PrimerFalloTecnico_VuelveAPendienteConNextAttemptAt()
    {
        var ct = TestContext.Current.CancellationToken;
        var (lote, item) = Lote();
        _entregador.EntregarAsync(Arg.Any<LoteItemEntregaRequest>(), Arg.Any<CancellationToken>())
            .Returns(LoteItemEntregaResult.Fallo("storage_unavailable"));

        var r = await Handler().HandleAsync(Cmd(lote, item, maxIntentos: 3, espera: 30), ct);

        r.Desenlace.Should().Be(ProcesarItemLoteDesenlace.Reprogramado);
        r.Intentos.Should().Be(1);
        r.SiguienteIntentoEn.Should().Be(Ahora.AddSeconds(30));
        await _proceso.Received(1).ReprogramarAsync(
            new LoteItemReintento(lote.Id, item.Id, 1, Ahora.AddSeconds(30)), Arg.Any<CancellationToken>());
        await _proceso.DidNotReceive().MarcarOmitidoAsync(Arg.Any<LoteItemOmitido>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AC4_AlAgotarIntentos_OmiteConErrorTecnicoYTextoLegible()
    {
        var ct = TestContext.Current.CancellationToken;
        var (lote, item) = Lote();
        item.Attempts = 2;
        _entregador.EntregarAsync(Arg.Any<LoteItemEntregaRequest>(), Arg.Any<CancellationToken>())
            .Returns(LoteItemEntregaResult.Fallo("storage_unavailable"));

        var r = await Handler().HandleAsync(Cmd(lote, item, maxIntentos: 3, espera: 30), ct);

        r.Desenlace.Should().Be(ProcesarItemLoteDesenlace.Omitido);
        r.Codigo.Should().Be(ConsolidadoLoteOmisiones.ErrorTecnico);
        r.Motivo.Should().Be("No se pudo generar el consolidado, intente de nuevo");
        await _proceso.Received(1).MarcarOmitidoAsync(
            new LoteItemOmitido(lote.Id, item.Id, ConsolidadoLoteOmisiones.ErrorTecnico,
                "No se pudo generar el consolidado, intente de nuevo", 3, Ahora),
            Arg.Any<CancellationToken>());
        await _proceso.DidNotReceive().ReprogramarAsync(Arg.Any<LoteItemReintento>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AC4_Edge_ExcepcionDelEntregador_SeReintenta_YLaCancelacionSePropaga()
    {
        var ct = TestContext.Current.CancellationToken;
        var (lote, item) = Lote();
        _entregador.EntregarAsync(Arg.Any<LoteItemEntregaRequest>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new IOException("e/s (simulado)"));

        var r = await Handler().HandleAsync(Cmd(lote, item), ct);
        r.Desenlace.Should().Be(ProcesarItemLoteDesenlace.Reprogramado);

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var cancelado = () => Handler().HandleAsync(Cmd(lote, item), cts.Token);
        _acceso.TieneAccesoAsync(Arg.Any<LoteItemContexto>(), cts.Token).ThrowsAsync(new OperationCanceledException(cts.Token));
        await cancelado.Should().ThrowAsync<OperationCanceledException>();
    }

    // ── AC5 — reintento tras una generación ya persistida ──────────────────────────────────────

    [Fact]
    public async Task AC5_ReintentoConConsolidadoYaPersistido_LoTomaComoExistente_SinGenerar()
    {
        var ct = TestContext.Current.CancellationToken;
        var repo = Substitute.For<IProcedureInstanceRepository>();
        var storage = new LoteFakeStorage();
        var merger = new LoteFakeMerger();
        var entregadorReal = new ConsolidadoLoteEntregador(
            repo,
            new GenerarConsolidadoHandler(repo, merger, storage),
            new GenerarConsolidadoMaestroHandler(repo, merger, storage));

        var (lote, item) = Lote();
        item.Attempts = 1; // el intento anterior cayó tras persistir el consolidado
        var instance = new ProcedureInstance
        {
            ProcedureType = ProcedureTypeFixture.For("matricula_inicial"),
            Id = item.ProcedureInstanceId,
            TenantId = item.TenantId,
            ProcedureTypeId = Guid.NewGuid(),
            ReferenceNumber = "TRM-2026-013375",
            Status = TramiteEstado.Entregado,
            CreatedAt = Ahora,
        };
        var persistido = new ProcedureInstanceAttachment
        {
            Id = Guid.NewGuid(),
            TenantId = instance.TenantId,
            ProcedureInstanceId = instance.Id,
            Tipo = "consolidado",
            Filename = "consolidado.pdf",
            Mimetype = "application/pdf",
            SizeBytes = 2048,
            Sha256 = "sha-persistido",
            StoragePath = "fm/persistido",
            Source = "system",
            UploadedAt = Ahora.AddMinutes(-1),
        };
        instance.Attachments.Add(persistido);
        repo.GetByIdWithAttachmentsAsync(instance.Id, instance.TenantId, Arg.Any<CancellationToken>()).Returns(instance);

        var r = await Handler(entregadorReal).HandleAsync(Cmd(lote, item), ct);

        r.Desenlace.Should().Be(ProcesarItemLoteDesenlace.Incluido);
        r.DeliveryMode.Should().Be(LoteDeliveryMode.Existente);
        await _proceso.Received(1).MarcarIncluidoAsync(
            Arg.Is<LoteItemIncluido>(c => c.Adjunto.AttachmentId == persistido.Id
                && c.Adjunto.StoragePath == persistido.StoragePath && c.Adjunto.SizeBytes == persistido.SizeBytes
                && c.DeliveryMode == LoteDeliveryMode.Existente),
            Arg.Any<CancellationToken>());
        storage.Saved.Should().BeEmpty("no genera otro consolidado");
        await repo.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    // ── AC6 — revalidación según el origen ──────────────────────────────────────────────────────

    [Fact]
    public async Task AC6_ElContextoLlevaOrigenCompaniaDelLoteYOrganismo()
    {
        var ct = TestContext.Current.CancellationToken;
        var (lote, item) = Lote();
        LoteItemContexto? visto = null;
        _acceso.TieneAccesoAsync(Arg.Do<LoteItemContexto>(c => visto = c), Arg.Any<CancellationToken>()).Returns(false);

        await Handler().HandleAsync(Cmd(lote, item), ct);

        visto.Should().NotBeNull();
        visto!.Origen.Should().Be(ConsolidadoExportOrigin.Tramites);
        visto.CompaniaLoteId.Should().Be(lote.TenantId);
        visto.CompaniaCongelada.Should().Be(lote.TenantId!.Value);
        visto.CompaniaTramiteId.Should().Be(item.TenantId);
        visto.OrganismoId.Should().BeNull();
        visto.SolicitanteId.Should().Be(lote.RequestedByUserId);
        visto.RolSolicitante.Should().Be(lote.RequestedRoleCode);
        visto.ProcedureInstanceId.Should().Be(item.ProcedureInstanceId);
        visto.BatchId.Should().Be(lote.Id);
        visto.ItemId.Should().Be(item.Id);
    }

    [Fact]
    public async Task AC6_LoteDeSuperAdmin_LaCompaniaCongeladaEsLaDelTramite()
    {
        var ct = TestContext.Current.CancellationToken;
        var (lote, item) = Lote(origen: ConsolidadoExportOrigin.Superadmin);
        LoteItemContexto? visto = null;
        _acceso.TieneAccesoAsync(Arg.Do<LoteItemContexto>(c => visto = c), Arg.Any<CancellationToken>()).Returns(true);
        _entregador.EntregarAsync(Arg.Any<LoteItemEntregaRequest>(), Arg.Any<CancellationToken>())
            .Returns(LoteItemEntregaResult.Incluido(Adjunto, LoteDeliveryMode.Existente));

        var r = await Handler().HandleAsync(Cmd(lote, item), ct);

        r.Desenlace.Should().Be(ProcesarItemLoteDesenlace.Incluido);
        visto!.Origen.Should().Be(ConsolidadoExportOrigin.Superadmin);
        visto.CompaniaLoteId.Should().BeNull();
        visto.CompaniaCongelada.Should().Be(item.TenantId);
    }

    [Fact]
    public async Task AC6_Contrato_OrigenSinProcesadorRegistrado_FallaSinTocarElItem()
    {
        var ct = TestContext.Current.CancellationToken;
        var (lote, item) = Lote(origen: ConsolidadoExportOrigin.OtBandeja);

        var act = () => Handler().HandleAsync(Cmd(lote, item), ct);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*ot_bandeja*");
        await _proceso.DidNotReceiveWithAnyArgs().MarcarOmitidoAsync(default!, ct);
        await _proceso.DidNotReceiveWithAnyArgs().ReprogramarAsync(default!, ct);
    }

    [Fact]
    public void AC6_Contrato_ElItemDeOtroLote_SeRechaza()
    {
        var (lote, _) = Lote();
        var ajeno = new ConsolidadoExportBatchItem { Id = Guid.NewGuid(), BatchId = Guid.NewGuid() };

        var act = () => LoteItemContexto.Desde(lote, ajeno);

        act.Should().Throw<ArgumentException>();
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    private static ProcesarItemLoteCommand Cmd(
        ConsolidadoExportBatch lote, ConsolidadoExportBatchItem item, short maxIntentos = 3, int espera = 30) =>
        ProcesarItemLoteCommand.Con(lote, item, new ConsolidadoExportSettings { MaxItemAttempts = maxIntentos, RetryDelaySeconds = espera });

    private static (ConsolidadoExportBatch Lote, ConsolidadoExportBatchItem Item) Lote(
        string origen = ConsolidadoExportOrigin.Tramites, string tipo = ConsolidadoExportDocumentType.Consolidado)
    {
        var compania = Guid.NewGuid();
        var lote = new ConsolidadoExportBatch
        {
            Id = Guid.NewGuid(),
            TenantId = origen == ConsolidadoExportOrigin.Superadmin ? null : compania,
            RequestedByUserId = Guid.NewGuid(),
            RequestedRoleCode = origen == ConsolidadoExportOrigin.Superadmin ? "SuperAdmin" : "Radicador",
            Origin = origen,
            DocumentType = tipo,
            OtTransitOfficeId = origen == ConsolidadoExportOrigin.OtBandeja ? Guid.NewGuid() : null,
            Status = ConsolidadoExportStatus.EnProceso,
        };
        return (lote, Item(lote, compania));
    }

    private static ConsolidadoExportBatchItem Item(ConsolidadoExportBatch lote, Guid compania) => new()
    {
        Id = Guid.NewGuid(),
        BatchId = lote.Id,
        TenantId = lote.TenantId ?? compania,
        ProcedureInstanceId = Guid.NewGuid(),
        Status = ConsolidadoExportItemStatus.Procesando,
        ReferenceNumber = "TRM-2026-013375",
        LeaseUntil = Ahora.AddMinutes(10),
    };

    private sealed class RelojFijo(DateTimeOffset ahora) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => ahora;
    }
}
