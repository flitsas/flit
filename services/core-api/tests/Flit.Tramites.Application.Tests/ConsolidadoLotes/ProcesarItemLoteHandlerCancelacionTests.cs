using Flit.Tramites.Application.UseCases.ConsolidadoLotes;
using Flit.Tramites.Domain.Entities.ConsolidadoLotes;
using Flit.Tramites.Domain.Repositories;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace Flit.Tramites.Application.Tests.ConsolidadoLotes;

/// <summary>
/// HU #13386 (Feature #13307, diseño 09 §2.4, CF-09) — el carril de ítems ante un lote cancelado:
/// <list type="bullet">
///   <item>AC1: checkpoint previo al entregador (<see cref="IConsolidadoLoteRepository.GetStatusAsync"/>, una lectura sin
///   lock). Lote cancelado, terminal o borrado ⇒ <see cref="ProcesarItemLoteDesenlace.LoteDetenido"/> sin llamar al
///   entregador (ni, por tanto, al generador) y sin escribir el ítem.</item>
///   <item>AC2: el ítem que ya estaba generando termina; si su cierre condicionado actualiza 0 filas se registra un log
///   sin PII (ids, no placa ni radicado).</item>
/// </list>
/// El acceso, el entregador, la persistencia y el repositorio del lote son sustitutos; los orígenes son los reales.
/// <para>Uso de ejemplo: <c>var r = await handler.HandleAsync(ProcesarItemLoteCommand.Con(lote, item, settings), ct);</c> ⇒
/// <c>r.Desenlace == LoteDetenido</c> si el lote se canceló antes del entregador.</para>
/// </summary>
public sealed class ProcesarItemLoteHandlerCancelacionTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 10, 7, 22, 0, 0, TimeSpan.Zero);
    private const string Placa = "QWE13386";
    private const string Radicado = "TRM-2026-013386";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly IConsolidadoLoteAccessChecker _acceso = Substitute.For<IConsolidadoLoteAccessChecker>();
    private readonly ILoteItemEntregador _entregador = Substitute.For<ILoteItemEntregador>();
    private readonly IConsolidadoLoteItemProceso _proceso = Substitute.For<IConsolidadoLoteItemProceso>();
    private readonly IConsolidadoLoteRepository _lotes = Substitute.For<IConsolidadoLoteRepository>();
    private readonly LogCapturado _log = new();

    private static readonly LoteItemAdjunto Adjunto = new(Guid.NewGuid(), "fm/consolidado-13386", 2048, "sha-13386", "consolidado.pdf");

    public ProcesarItemLoteHandlerCancelacionTests()
    {
        _acceso.TieneAccesoAsync(Arg.Any<LoteItemContexto>(), Arg.Any<CancellationToken>()).Returns(true);
        _entregador.EntregarAsync(Arg.Any<LoteItemEntregaRequest>(), Arg.Any<CancellationToken>())
            .Returns(LoteItemEntregaResult.Incluido(Adjunto, LoteDeliveryMode.Generado));
    }

    private ProcesarItemLoteHandler Handler() => new(
        new LoteItemOrigenPorOrigen([new TramitesLoteItemOrigen(_acceso, _entregador), new SuperAdminLoteItemOrigen(_acceso, _entregador)]),
        _proceso,
        _log,
        _lotes,
        new RelojFijo(Ahora));

    private void EstadoDelLote(string? estado) =>
        _lotes.GetStatusAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(estado);

    // ── AC1 — checkpoint previo al entregador ─────────────────────────────────────────────────────────

    [Fact]
    public async Task AC1_LoteCanceladoAntesDelEntregador_TerminaSinEntregarNiGenerar_YNoEscribeElItem()
    {
        var (lote, item) = Lote();
        EstadoDelLote(ConsolidadoExportStatus.Cancelado);

        var r = await Handler().HandleAsync(Cmd(lote, item), Ct);

        r.Desenlace.Should().Be(ProcesarItemLoteDesenlace.LoteDetenido);
        r.Aplicado.Should().BeFalse("no se escribe nada: la cancelación ya dejó el ítem en cancelado");
        r.Intentos.Should().Be(item.Attempts, "un lote detenido no consume intentos");
        await _entregador.DidNotReceiveWithAnyArgs().EntregarAsync(default!, Ct);
        _proceso.ReceivedCalls().Should().BeEmpty("ni incluido, ni omitido, ni reprogramado");
        await _lotes.Received(1).GetStatusAsync(lote.Id, Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(ConsolidadoExportStatus.Fallido)]
    [InlineData(ConsolidadoExportStatus.Completado)]
    [InlineData(ConsolidadoExportStatus.Expirado)]
    [InlineData(null)]
    public async Task AC1_Borde_LoteTerminalOBorrado_TambienSeDetieneAntesDelEntregador(string? estado)
    {
        var (lote, item) = Lote();
        EstadoDelLote(estado);

        var r = await Handler().HandleAsync(Cmd(lote, item), Ct);

        r.Desenlace.Should().Be(ProcesarItemLoteDesenlace.LoteDetenido);
        await _entregador.DidNotReceiveWithAnyArgs().EntregarAsync(default!, Ct);
        _proceso.ReceivedCalls().Should().BeEmpty();
    }

    [Theory]
    [InlineData(ConsolidadoExportStatus.EnCola)]
    [InlineData(ConsolidadoExportStatus.EnProceso)]
    public async Task AC1_Contrato_LoteActivo_PasaElCheckpoint_DespuesDeRevalidarElAcceso_YEntregaUnaVez(string estado)
    {
        var (lote, item) = Lote();
        EstadoDelLote(estado);
        _proceso.MarcarIncluidoAsync(Arg.Any<LoteItemIncluido>(), Arg.Any<CancellationToken>()).Returns(true);

        var r = await Handler().HandleAsync(Cmd(lote, item), Ct);

        r.Desenlace.Should().Be(ProcesarItemLoteDesenlace.Incluido);
        r.Aplicado.Should().BeTrue();
        Received.InOrder(() =>
        {
            _acceso.TieneAccesoAsync(Arg.Any<LoteItemContexto>(), Arg.Any<CancellationToken>());
            _lotes.GetStatusAsync(lote.Id, Arg.Any<CancellationToken>());
            _entregador.EntregarAsync(Arg.Any<LoteItemEntregaRequest>(), Arg.Any<CancellationToken>());
        });
        await _lotes.Received(1).GetStatusAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        _log.Entradas.Should().NotContain(e => e.Nivel == LogLevel.Warning, "un cierre aplicado no es anómalo");
    }

    // ── AC2 — ítem en vuelo al cancelar ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task AC2_ItemEnVueloAlCancelar_GeneraYElCierreActualiza0Filas_LogSinPii()
    {
        var (lote, item) = Lote();
        // El checkpoint ve el lote activo: la cancelación llega mientras el generador trabaja.
        EstadoDelLote(ConsolidadoExportStatus.EnProceso);
        _proceso.MarcarIncluidoAsync(Arg.Any<LoteItemIncluido>(), Arg.Any<CancellationToken>()).Returns(false);

        var r = await Handler().HandleAsync(Cmd(lote, item), Ct);

        r.Desenlace.Should().Be(ProcesarItemLoteDesenlace.Incluido, "el consolidado generado queda oficial (CF-09)");
        r.Aplicado.Should().BeFalse("el ítem ya era cancelado: el cierre condicionado no escribe");
        await _entregador.Received(1).EntregarAsync(Arg.Any<LoteItemEntregaRequest>(), Arg.Any<CancellationToken>());
        var aviso = _log.Entradas.Should().ContainSingle(e => e.Nivel == LogLevel.Warning).Subject;
        aviso.Mensaje.Should().Contain(lote.Id.ToString()).And.Contain(item.Id.ToString()).And.Contain("0 filas");
        _log.Entradas.Should().AllSatisfy(e =>
        {
            e.Mensaje.Should().NotContain(Placa);
            e.Mensaje.Should().NotContain(Radicado);
        });
    }

    [Fact]
    public async Task AC2_Borde_OmitidoEnVueloAlCancelar_TambienAvisaElCierreSinEfecto()
    {
        var (lote, item) = Lote();
        EstadoDelLote(ConsolidadoExportStatus.EnProceso);
        _entregador.EntregarAsync(Arg.Any<LoteItemEntregaRequest>(), Arg.Any<CancellationToken>())
            .Returns(LoteItemEntregaResult.Omitido(ConsolidadoLoteOmisiones.SinAdjuntos));
        _proceso.MarcarOmitidoAsync(Arg.Any<LoteItemOmitido>(), Arg.Any<CancellationToken>()).Returns(false);

        var r = await Handler().HandleAsync(Cmd(lote, item), Ct);

        r.Desenlace.Should().Be(ProcesarItemLoteDesenlace.Omitido);
        r.Aplicado.Should().BeFalse();
        _log.Entradas.Should().ContainSingle(e => e.Nivel == LogLevel.Warning && e.Mensaje.Contains("0 filas"));
    }

    // ── Apoyo ─────────────────────────────────────────────────────────────────────────────────────────

    private static ProcesarItemLoteCommand Cmd(ConsolidadoExportBatch lote, ConsolidadoExportBatchItem item) =>
        ProcesarItemLoteCommand.Con(lote, item, new ConsolidadoExportSettings { MaxItemAttempts = 3, RetryDelaySeconds = 30 });

    private static (ConsolidadoExportBatch Lote, ConsolidadoExportBatchItem Item) Lote()
    {
        var compania = Guid.NewGuid();
        var lote = new ConsolidadoExportBatch
        {
            Id = Guid.NewGuid(),
            TenantId = compania,
            RequestedByUserId = Guid.NewGuid(),
            RequestedRoleCode = "Radicador",
            Origin = ConsolidadoExportOrigin.Tramites,
            DocumentType = ConsolidadoExportDocumentType.Consolidado,
            Status = ConsolidadoExportStatus.EnProceso,
        };
        var item = new ConsolidadoExportBatchItem
        {
            Id = Guid.NewGuid(),
            BatchId = lote.Id,
            TenantId = compania,
            ProcedureInstanceId = Guid.NewGuid(),
            Status = ConsolidadoExportItemStatus.Procesando,
            ReferenceNumber = Radicado,
            Plate = Placa,
            Attempts = 1,
            LeaseUntil = Ahora.AddMinutes(10),
        };
        return (lote, item);
    }
}

/// <summary>HU #13386 — repositorio del lote cuyo checkpoint devuelve un estado fijo.</summary>
/// <remarks>Uso de ejemplo: <c>new ProcesarItemLoteHandler(origenes, proceso, logger, LotesEnEstado.Con("en_proceso"))</c>.</remarks>
internal static class LotesEnEstado
{
    public static IConsolidadoLoteRepository Con(string? estado)
    {
        var lotes = Substitute.For<IConsolidadoLoteRepository>();
        lotes.GetStatusAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(estado);
        return lotes;
    }
}

/// <summary>HU #13386 — logger que guarda el mensaje ya formateado (para afirmar que no lleva PII).</summary>
internal sealed class LogCapturado : ILogger<ProcesarItemLoteHandler>, ILogger<EmpaquetarParteHandler>
{
    public List<(LogLevel Nivel, string Mensaje)> Entradas { get; } = [];

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        lock (Entradas)
            Entradas.Add((logLevel, formatter(state, exception)));
    }
}
