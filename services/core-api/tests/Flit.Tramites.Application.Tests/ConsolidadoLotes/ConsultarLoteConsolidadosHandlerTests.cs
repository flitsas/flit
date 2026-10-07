using Flit.Tramites.Application.UseCases.ConsolidadoLotes;
using Flit.Tramites.Domain.Entities.ConsolidadoLotes;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Tramites.Application.Tests.ConsolidadoLotes;

/// <summary>
/// HU #13379 (Épica #13216, AC1/AC3) — <see cref="ConsultarLoteConsolidadosHandler"/>: lote actual (activo o último
/// retenido) y por id, solo del dueño (<c>sub</c>), con las partes descargables (número, nombre CF-13 y tamaño en claro)
/// solo cuando el lote es descargable.
/// <para>Uso de ejemplo: <c>await handler.ActualAsync(new ObtenerLoteActualQuery(sub), ct)</c> ⇒ <c>null</c> = 204.</para>
/// </summary>
public sealed class ConsultarLoteConsolidadosHandlerTests
{
    private static readonly Guid Dueno = Guid.NewGuid();
    private readonly IConsolidadoLoteLectura _lectura = Substitute.For<IConsolidadoLoteLectura>();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private ConsultarLoteConsolidadosHandler Handler() => new(_lectura, new RelojFijo(LoteConsulta.Ahora));

    [Fact]
    public async Task AC1_LoteActivo_DevuelveElLoteSinPartes_YNoConsultaPartes()
    {
        var lote = LoteConsulta.Lote(ConsolidadoExportStatus.EnProceso, Dueno, partes: 0);
        _lectura.ObtenerActualDelDuenoAsync(Dueno, Arg.Any<CancellationToken>()).Returns(lote);

        var r = await Handler().ActualAsync(new ObtenerLoteActualQuery(Dueno), Ct);

        r!.Lote.Should().BeSameAs(lote);
        r.Partes.Should().BeEmpty("las partes solo se ofrecen cuando el lote termina (CF-09)");
        r.NombreBase.Should().Be("consolidados_20261006_1430");
        await _lectura.DidNotReceiveWithAnyArgs().ObtenerPartesAsync(default, Ct);
    }

    [Fact]
    public async Task AC1_SinLoteActivoNiRetenido_Null()
    {
        _lectura.ObtenerActualDelDuenoAsync(Dueno, Arg.Any<CancellationToken>()).Returns((ConsolidadoExportBatch?)null);

        (await Handler().ActualAsync(new ObtenerLoteActualQuery(Dueno), Ct)).Should().BeNull();
    }

    [Fact]
    public async Task AC1_TerminadoRetenido_PartesCerradasConNumeroNombreYBytesEnClaro()
    {
        var lote = LoteConsulta.Lote(ConsolidadoExportStatus.CompletadoConOmitidos, Dueno, partes: 2);
        _lectura.ObtenerActualDelDuenoAsync(Dueno, Arg.Any<CancellationToken>()).Returns(lote);
        _lectura.ObtenerPartesAsync(lote.Id, Arg.Any<CancellationToken>())
            .Returns([LoteConsulta.Parte(lote, 2, bytes: 2222), LoteConsulta.Parte(lote, 1, bytes: 1111)]);

        var r = await Handler().ActualAsync(new ObtenerLoteActualQuery(Dueno), Ct);

        r!.Partes.Should().Equal(
            new ParteConsultada(1, "consolidados_20261006_1430_parte-01-de-02.zip", 4, 1, 1111),
            new ParteConsultada(2, "consolidados_20261006_1430_parte-02-de-02.zip", 4, 1, 2222));
        r.Lote.ExpiresAt.Should().Be(LoteConsulta.Ahora.AddHours(23));
    }

    [Fact]
    public async Task AC1_UnaSolaParte_NombreSinSufijoDeParte()
    {
        var lote = LoteConsulta.Lote(dueno: Dueno);
        _lectura.ObtenerDelDuenoAsync(lote.Id, Dueno, Arg.Any<CancellationToken>()).Returns(lote);
        _lectura.ObtenerPartesAsync(lote.Id, Arg.Any<CancellationToken>()).Returns([LoteConsulta.Parte(lote, 1)]);

        var r = await Handler().PorIdAsync(new ObtenerLoteQuery(lote.Id, Dueno), Ct);

        r!.Partes.Should().ContainSingle().Which.NombreArchivo.Should().Be("consolidados_20261006_1430.zip");
    }

    [Theory]
    [InlineData(ConsolidadoExportStatus.Fallido, false)]
    [InlineData(ConsolidadoExportStatus.Completado, true)]
    public async Task AC1_FallidoOVencidoSinPurgar_SeDevuelveSinPartes(string estado, bool vencido)
    {
        var lote = LoteConsulta.Lote(estado, Dueno, expira: vencido ? LoteConsulta.Ahora : null);
        _lectura.ObtenerActualDelDuenoAsync(Dueno, Arg.Any<CancellationToken>()).Returns(lote);
        _lectura.ObtenerPartesAsync(lote.Id, Arg.Any<CancellationToken>()).Returns([LoteConsulta.Parte(lote, 1)]);

        var r = await Handler().ActualAsync(new ObtenerLoteActualQuery(Dueno), Ct);

        r!.Lote.Status.Should().Be(estado);
        r.Partes.Should().BeEmpty("un lote fallido o vencido no ofrece partes descargables");
    }

    [Fact]
    public async Task AC1_PartesNoCerradas_NoSeOfrecen()
    {
        var lote = LoteConsulta.Lote(dueno: Dueno, partes: 2);
        _lectura.ObtenerDelDuenoAsync(lote.Id, Dueno, Arg.Any<CancellationToken>()).Returns(lote);
        _lectura.ObtenerPartesAsync(lote.Id, Arg.Any<CancellationToken>())
            .Returns([LoteConsulta.Parte(lote, 1), LoteConsulta.Parte(lote, 2, ConsolidadoExportPartStatus.Purgada)]);

        var r = await Handler().PorIdAsync(new ObtenerLoteQuery(lote.Id, Dueno), Ct);

        r!.Partes.Select(p => p.Numero).Should().Equal(1);
    }

    [Fact]
    public async Task AC3_PorId_DeOtroUsuario_Null_PorqueLaLecturaFiltraPorElDueno()
    {
        var lote = LoteConsulta.Lote(dueno: Dueno);
        var otro = Guid.NewGuid();
        _lectura.ObtenerDelDuenoAsync(lote.Id, Dueno, Arg.Any<CancellationToken>()).Returns(lote);
        _lectura.ObtenerDelDuenoAsync(lote.Id, otro, Arg.Any<CancellationToken>()).Returns((ConsolidadoExportBatch?)null);

        (await Handler().PorIdAsync(new ObtenerLoteQuery(lote.Id, otro), Ct)).Should().BeNull();
        await _lectura.Received(1).ObtenerDelDuenoAsync(lote.Id, otro, Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(ConsolidadoExportStatus.EnCola, ConsolidadoLoteDescargable.NoTerminado)]
    [InlineData(ConsolidadoExportStatus.EnProceso, ConsolidadoLoteDescargable.NoTerminado)]
    [InlineData(ConsolidadoExportStatus.Empaquetando, ConsolidadoLoteDescargable.NoTerminado)]
    [InlineData(ConsolidadoExportStatus.Completado, ConsolidadoLoteDescargable.Si)]
    [InlineData(ConsolidadoExportStatus.CompletadoConOmitidos, ConsolidadoLoteDescargable.Si)]
    [InlineData(ConsolidadoExportStatus.Fallido, ConsolidadoLoteDescargable.SinPartes)]
    [InlineData(ConsolidadoExportStatus.Cancelado, ConsolidadoLoteDescargable.SinPartes)]
    public void Contrato_Descargabilidad_PorEstado(string estado, ConsolidadoLoteDescargable esperado)
    {
        ConsolidadoLoteDescargabilidad.Evaluar(LoteConsulta.Lote(estado), LoteConsulta.Ahora).Should().Be(esperado);
    }

    [Fact]
    public void Contrato_Descargabilidad_PurgadoOVencidoOSinDek_Expirado()
    {
        var purgado = LoteConsulta.Lote(ConsolidadoExportStatus.Expirado, purgado: LoteConsulta.Ahora.AddHours(-1));
        var vencido = LoteConsulta.Lote(expira: LoteConsulta.Ahora);
        var sinDek = LoteConsulta.Lote();
        sinDek.DekWrapped = null;

        ConsolidadoLoteDescargabilidad.Evaluar(purgado, LoteConsulta.Ahora).Should().Be(ConsolidadoLoteDescargable.Expirado);
        ConsolidadoLoteDescargabilidad.Evaluar(vencido, LoteConsulta.Ahora).Should().Be(ConsolidadoLoteDescargable.Expirado);
        ConsolidadoLoteDescargabilidad.Evaluar(sinDek, LoteConsulta.Ahora).Should().Be(ConsolidadoLoteDescargable.Expirado);
    }
}
