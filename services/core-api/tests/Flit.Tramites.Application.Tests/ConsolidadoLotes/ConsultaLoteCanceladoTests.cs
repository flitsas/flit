using Flit.Tramites.Application.UseCases.ConsolidadoLotes;
using Flit.Tramites.Domain.Entities.ConsolidadoLotes;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Tramites.Application.Tests.ConsolidadoLotes;

/// <summary>
/// HU #13386 AC6 (diseño 09 §2.4, CF-11) — el lote cancelado en la consulta y en la descarga. Con lectura y almacenamiento
/// sustitutos (la consulta real contra PostgreSQL la cubre <c>ConsolidadoLoteCancelacionCarrilesIntegrationTests</c>):
/// <list type="bullet">
///   <item><c>actual</c> devuelve el lote <c>cancelado</c> tal cual (estado, <c>finished_at</c> = instante de la cancelación,
///   contadores) con <c>partes</c> vacío y sin consultar partes.</item>
///   <item>La descarga de cualquier parte de un lote cancelado es <see cref="DescargarParteEstado.Expirada"/> (410
///   <c>descarga_expirada</c>): ya se purgó; sin abrir el objeto ni auditar.</item>
/// </list>
/// </summary>
/// <remarks>Uso de ejemplo: <c>ConsolidadoLoteDescargabilidad.Evaluar(loteCancelado, ahora)</c> ⇒ <c>Expirado</c>.</remarks>
public sealed class ConsultaLoteCanceladoTests
{
    private static readonly Guid Dueno = Guid.NewGuid();
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly IConsolidadoLoteLectura _lectura = Substitute.For<IConsolidadoLoteLectura>();
    private readonly IConsolidadoLoteParteStorage _storage = Substitute.For<IConsolidadoLoteParteStorage>();
    private readonly IConsolidadoLoteCipher _cifrador = Substitute.For<IConsolidadoLoteCipher>();

    /// <summary>Lote como lo deja la cancelación (#13385): DEK NULL y finished/expires/purged = instante de la cancelación.</summary>
    private static ConsolidadoExportBatch Cancelado()
    {
        var lote = LoteConsulta.Lote(ConsolidadoExportStatus.Cancelado, Dueno, partes: 2);
        var instante = LoteConsulta.Ahora.AddMinutes(-3);
        lote.FinishedAt = instante;
        lote.ExpiresAt = instante;
        lote.PurgedAt = instante;
        lote.DekWrapped = null;
        return lote;
    }

    [Fact]
    public void AC6_Contrato_LoteCancelado_EsExpirado_NoSinPartes()
    {
        ConsolidadoLoteDescargabilidad.Evaluar(Cancelado(), LoteConsulta.Ahora).Should().Be(ConsolidadoLoteDescargable.Expirado);
        // Aun con DEK y sin purgar (estado que la cancelación nunca deja), cancelado no es descargable.
        ConsolidadoLoteDescargabilidad.Evaluar(LoteConsulta.Lote(ConsolidadoExportStatus.Cancelado), LoteConsulta.Ahora)
            .Should().Be(ConsolidadoLoteDescargable.Expirado);
    }

    [Fact]
    public async Task AC6_Actual_LoteCancelado_SeDevuelveConTerminadoEnYContadoresCongelados_YPartesVacio()
    {
        var lote = Cancelado();
        _lectura.ObtenerActualDelDuenoAsync(Dueno, Arg.Any<CancellationToken>()).Returns(lote);

        var r = await new ConsultarLoteConsolidadosHandler(_lectura, new RelojFijo(LoteConsulta.Ahora))
            .ActualAsync(new ObtenerLoteActualQuery(Dueno), Ct);

        r.Should().NotBeNull("el último lote cancelado se informa (CF-11), no es 204");
        r!.Lote.Status.Should().Be(ConsolidadoExportStatus.Cancelado);
        r.Lote.FinishedAt.Should().Be(LoteConsulta.Ahora.AddMinutes(-3));
        (r.Lote.TotalItems, r.Lote.IncludedCount, r.Lote.OmittedCount, r.Lote.GeneratedCount).Should().Be((10, 7, 2, 3));
        r.Partes.Should().BeEmpty();
        await _lectura.DidNotReceiveWithAnyArgs().ObtenerPartesAsync(default, Ct);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(7)]
    public async Task AC6_Descarga_DeCualquierParteDeUnLoteCancelado_Expirada_SinAbrirNiAuditar(int numero)
    {
        var lote = Cancelado();
        _lectura.ObtenerDelDuenoAsync(lote.Id, Dueno, Arg.Any<CancellationToken>()).Returns(lote);

        var r = await new DescargarParteHandler(_lectura, _storage, _cifrador, reloj: new RelojFijo(LoteConsulta.Ahora))
            .PrepararAsync(new DescargarParteQuery(lote.Id, numero, Dueno, "Radicador"), Ct);

        r.Estado.Should().Be(DescargarParteEstado.Expirada);
        r.Descarga.Should().BeNull();
        await _storage.DidNotReceiveWithAnyArgs().OpenReadAsync(default!, Ct);
        await _lectura.DidNotReceiveWithAnyArgs().RegistrarDescargaAsync(default!, Ct);
        await _lectura.DidNotReceiveWithAnyArgs().ObtenerPartesAsync(default, Ct);
    }

    [Fact]
    public async Task AC6_Borde_LoteDeOtroUsuario_SigueSiendo404_AunqueEsteCancelado()
    {
        var lote = Cancelado();
        var otro = Guid.NewGuid();
        _lectura.ObtenerDelDuenoAsync(lote.Id, otro, Arg.Any<CancellationToken>()).Returns((ConsolidadoExportBatch?)null);

        var r = await new DescargarParteHandler(_lectura, _storage, _cifrador, reloj: new RelojFijo(LoteConsulta.Ahora))
            .PrepararAsync(new DescargarParteQuery(lote.Id, 1, otro, "Radicador"), Ct);

        r.Estado.Should().Be(DescargarParteEstado.NoEncontrada);
    }
}
