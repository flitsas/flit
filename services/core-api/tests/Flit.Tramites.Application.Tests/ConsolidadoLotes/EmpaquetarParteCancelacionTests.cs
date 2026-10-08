using Flit.Tramites.Application.Storage;
using Flit.Tramites.Application.UseCases.ConsolidadoLotes;
using Flit.Tramites.Domain.Entities.ConsolidadoLotes;
using Flit.Tramites.Domain.Repositories;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Tramites.Application.Tests.ConsolidadoLotes;

/// <summary>
/// HU #13386 (Feature #13307, diseño 09 §2.4, CF-09) — checkpoints cooperativos de <see cref="EmpaquetarParteHandler"/> ante
/// un lote cancelado. El estado se lee con <see cref="IConsolidadoLoteRepository.GetStatusAsync"/> (sin lock) en tres
/// puntos: al reclamar, cada <see cref="EmpaquetarParteHandler.PdfsPorCheckpoint"/> PDF copiados y antes de subir (este
/// último, bajo lock, es el descarte de #13378). Si el lote ya no está vivo: aborta, borra sus temporales, no sube, la
/// parte queda <c>descartada</c> (confirmado bajo el lock del lote) y nada pasa a <c>fallida</c>/<c>fallido</c>.
/// </summary>
/// <remarks>
/// Uso de ejemplo:
/// <code>
/// var h = new EmpaquetarParteHandler(empaquetado, cipher, partes, adjuntos, actual, temporales, logger, lotes);
/// (await h.HandleAsync(reclamada, 3, ct)).Should().Be(EmpaquetarParteDesenlace.Descartada);
/// </code>
/// </remarks>
public sealed class EmpaquetarParteCancelacionTests : IDisposable
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "flit-13386-tests", Guid.NewGuid().ToString("N"));
    private readonly IConsolidadoLoteEmpaquetado _empaquetado = Substitute.For<IConsolidadoLoteEmpaquetado>();
    private readonly IConsolidadoLoteRepository _lotes = Substitute.For<IConsolidadoLoteRepository>();
    private readonly CipherIdentidad _cipher = new();
    private readonly IConsolidadoLoteParteStorage _partes = Substitute.For<IConsolidadoLoteParteStorage>();
    private readonly IAttachmentStorage _adjuntos = Substitute.For<IAttachmentStorage>();
    private readonly IConsolidadoLoteAdjuntoActual _actual = Substitute.For<IConsolidadoLoteAdjuntoActual>();

    public EmpaquetarParteCancelacionTests()
    {
        _adjuntos.OpenReadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult<Stream?>(new MemoryStream("pdf"u8.ToArray())));
        _partes.SubirAsync(Arg.Any<Guid>(), Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new StoredFile("fm-parte-1", new string('a', 64), 10));
        _empaquetado.CerrarParteAsync(Arg.Any<CierreParteLote>(), Arg.Any<CancellationToken>()).Returns(CierreParteDesenlace.Cerrada);
        _empaquetado.DescartarSiLoteInactivoAsync(default, default, default, default).ReturnsForAnyArgs(false);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    private EmpaquetarParteHandler Handler() => new(
        _empaquetado, _cipher, _partes, _adjuntos, _actual, new ConsolidadoLoteTemporales(_dir), new LogCapturado(), _lotes);

    private void Contenido(int pdfs) =>
        _empaquetado.LeerContenidoAsync(Arg.Any<Guid>(), Arg.Any<short>(), Arg.Any<CancellationToken>())
            .Returns(new ContenidoParteLote(
                [.. Enumerable.Range(0, pdfs).Select(i => new PdfDeParte(
                    Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), i, $"R-{i}", $"P{i}", $"fm/pdf-{i}"))],
                []));

    /// <summary>Estados que verá cada lectura del checkpoint, en orden (el último se repite).</summary>
    private void Estados(params string?[] estados) =>
        _lotes.GetStatusAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(estados[0], estados[1..]);

    private void CanceladoBajoLock() =>
        _empaquetado.DescartarSiLoteInactivoAsync(default, default, default, default).ReturnsForAnyArgs(true);

    private IEnumerable<string> Temporales() =>
        Directory.Exists(_dir) ? Directory.EnumerateFiles(_dir) : [];

    // ── Checkpoint 1: al reclamar ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AC4_CheckpointAlReclamar_LoteCancelado_AbortaSinLeerNiCifrarNiSubir_YQuedaDescartada()
    {
        var reclamada = Reclamada();
        Contenido(3);
        Estados(ConsolidadoExportStatus.Cancelado);
        CanceladoBajoLock();

        var desenlace = await Handler().HandleAsync(reclamada, 3, Ct);

        desenlace.Should().Be(EmpaquetarParteDesenlace.Descartada);
        await _empaquetado.DidNotReceiveWithAnyArgs().LeerContenidoAsync(default, default, Ct);
        _cipher.Llamadas.Should().Be(0);
        await _partes.DidNotReceiveWithAnyArgs().SubirAsync(default, default, default!, Ct);
        await _empaquetado.Received(1).DescartarSiLoteInactivoAsync(reclamada.Lote.Id, 1, 0, Arg.Any<CancellationToken>());
        await _lotes.Received(1).GetStatusAsync(reclamada.Lote.Id, Arg.Any<CancellationToken>());
        Temporales().Should().BeEmpty();
        await NadaFallido();
    }

    // ── Checkpoint 2: cada 50 PDF ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AC4_CheckpointCada50Pdf_CanceladoTrasLos50Primeros_DejaDeCopiar_BorraTemporales_YNoSube()
    {
        EmpaquetarParteHandler.PdfsPorCheckpoint.Should().Be(50);
        var reclamada = Reclamada();
        Contenido(120);
        Estados(ConsolidadoExportStatus.Empaquetando, ConsolidadoExportStatus.Cancelado);
        CanceladoBajoLock();

        var desenlace = await Handler().HandleAsync(reclamada, 3, Ct);

        desenlace.Should().Be(EmpaquetarParteDesenlace.Descartada);
        _adjuntos.ReceivedCalls().Count(c => c.GetMethodInfo().Name == nameof(IAttachmentStorage.OpenReadAsync))
            .Should().Be(50, "el checkpoint llega tras copiar el PDF número 50 y no se abre ninguno más");
        await _lotes.Received(2).GetStatusAsync(reclamada.Lote.Id, Arg.Any<CancellationToken>());
        _cipher.Llamadas.Should().Be(0, "no se cifra un ZIP abortado");
        await _partes.DidNotReceiveWithAnyArgs().SubirAsync(default, default, default!, Ct);
        await _empaquetado.DidNotReceiveWithAnyArgs().CerrarParteAsync(default!, Ct);
        Temporales().Should().BeEmpty("el ZIP a medias se borra");
        await NadaFallido();
    }

    [Theory]
    [InlineData(49, 1)]
    [InlineData(50, 2)]
    [InlineData(120, 3)]
    public async Task AC4_Contrato_LoteVivo_UnaLecturaAlReclamarYUnaPorCada50Pdf_YCierra(int pdfs, int lecturas)
    {
        var reclamada = Reclamada();
        Contenido(pdfs);
        Estados(ConsolidadoExportStatus.Empaquetando);

        var desenlace = await Handler().HandleAsync(reclamada, 3, Ct);

        desenlace.Should().Be(EmpaquetarParteDesenlace.Cerrada);
        await _lotes.Received(lecturas).GetStatusAsync(reclamada.Lote.Id, Arg.Any<CancellationToken>());
        await _partes.ReceivedWithAnyArgs(1).SubirAsync(default, default, default!, Ct);
        Temporales().Should().BeEmpty();
    }

    // ── Checkpoint 3: antes de subir (bajo lock, #13378) ──────────────────────────────────────────────

    [Fact]
    public async Task AC4_CheckpointAntesDeSubir_CanceladoTrasCifrar_NoSube_BorraElCifrado_YNoFalla()
    {
        var reclamada = Reclamada();
        Contenido(2);
        Estados(ConsolidadoExportStatus.Empaquetando);
        CanceladoBajoLock();

        var desenlace = await Handler().HandleAsync(reclamada, 3, Ct);

        desenlace.Should().Be(EmpaquetarParteDesenlace.Descartada);
        _cipher.Llamadas.Should().Be(1);
        await _partes.DidNotReceiveWithAnyArgs().SubirAsync(default, default, default!, Ct);
        Temporales().Should().BeEmpty("el cifrado sin subir también se borra");
        await NadaFallido();
    }

    // ── DEK ausente de un lote cancelado ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task AC4_Negativo_DekAusentePorCancelacion_NoMarcaLaParteFallidaNiElLoteFallido()
    {
        var reclamada = Reclamada();
        Contenido(1);
        Estados(ConsolidadoExportStatus.Empaquetando);
        _cipher.Error = ConsolidadoLoteCifradoError.DekAusente;
        CanceladoBajoLock();

        var desenlace = await Handler().HandleAsync(reclamada, 3, Ct);

        desenlace.Should().Be(EmpaquetarParteDesenlace.Descartada);
        Temporales().Should().BeEmpty();
        await NadaFallido();
    }

    private async Task NadaFallido()
    {
        await _empaquetado.DidNotReceiveWithAnyArgs().FallarLoteAsync(default, default!, Ct);
        await _empaquetado.DidNotReceiveWithAnyArgs().RegistrarFalloParteAsync(default!, Ct);
        await _empaquetado.DidNotReceiveWithAnyArgs().FinalizarLoteAsync(default, Ct);
    }

    private static ParteLoteReclamada Reclamada()
    {
        var lote = new ConsolidadoExportBatch
        {
            Id = Guid.CreateVersion7(),
            TenantId = Guid.NewGuid(),
            Status = ConsolidadoExportStatus.Empaquetando,
            DocumentType = ConsolidadoExportDocumentType.Consolidado,
            DekWrapped = [1, 2, 3],
        };
        return new ParteLoteReclamada(lote, new ConsolidadoExportBatchPart
        {
            Id = Guid.NewGuid(),
            BatchId = lote.Id,
            PartNumber = 1,
            Status = ConsolidadoExportPartStatus.Empaquetando,
            Attempts = 0,
            LeaseUntil = DateTimeOffset.UtcNow.AddMinutes(10),
        });
    }

    /// <summary>Cifrado de identidad que cuenta las llamadas (el FLZ1 real lo cubre <c>ConsolidadoLoteCipherTests</c>).</summary>
    private sealed class CipherIdentidad : IConsolidadoLoteCipher
    {
        public ConsolidadoLoteCifradoError? Error { get; set; }
        public int Llamadas { get; private set; }

        public byte[] GenerarDekEnvuelta() => [1];

        public async Task<ConsolidadoLoteCifradoResultado> CifrarAsync(
            byte[]? dekEnvuelta, Guid loteId, int partNumber, Stream origenEnClaro, Stream destinoCifrado, CancellationToken ct = default)
        {
            if (Error is { } e)
                throw new ConsolidadoLoteCifradoException(e);
            Llamadas++;
            var antes = destinoCifrado.Position;
            await origenEnClaro.CopyToAsync(destinoCifrado, ct);
            var bytes = destinoCifrado.Position - antes;
            return new ConsolidadoLoteCifradoResultado(bytes, bytes, 1);
        }

        public Task<long> DescifrarAsync(
            byte[]? dekEnvuelta, Guid loteId, int partNumber, Stream origenCifrado, Stream destinoEnClaro, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }
}
