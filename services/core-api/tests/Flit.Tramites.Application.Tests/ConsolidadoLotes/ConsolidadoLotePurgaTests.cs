using Flit.Tramites.Domain.Entities.ConsolidadoLotes;
using FluentAssertions;
using Xunit;

namespace Flit.Tramites.Application.Tests.ConsolidadoLotes;

/// <summary>
/// HU #13373 (AC5) — operación de dominio <see cref="ConsolidadoLotePurga"/>: borrado criptográfico de un lote terminal,
/// la misma que reutiliza la purga a las 24 h (#13379). La transacción real la cubre la integración.
/// </summary>
/// <remarks>
/// Uso de ejemplo:
/// <code>
/// ConsolidadoLotePurga.Aplicar(lote, partes, DateTimeOffset.UtcNow);
/// </code>
/// </remarks>
public sealed class ConsolidadoLotePurgaTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 10, 7, 15, 0, 0, TimeSpan.Zero);

    private static ConsolidadoExportBatch Lote(string status) => new()
    {
        Id = Guid.NewGuid(),
        Status = status,
        DekWrapped = [1, 2, 3],
        FinishedAt = ConsolidadoExportStatus.EsActivo(status) ? null : Ahora.AddHours(-2),
        ExpiresAt = ConsolidadoExportStatus.EsActivo(status) ? null : Ahora.AddHours(22),
    };

    private static ConsolidadoExportBatchPart Parte(Guid lote, short n, string status) =>
        new() { Id = Guid.NewGuid(), BatchId = lote, PartNumber = n, Status = status };

    [Theory]
    [InlineData(ConsolidadoExportStatus.Completado)]
    [InlineData(ConsolidadoExportStatus.CompletadoConOmitidos)]
    [InlineData(ConsolidadoExportStatus.Fallido)]
    public void AC5_LoteTerminal_QuedaExpirado_SinDek_YSusPartesCerradasPurgadas(string status)
    {
        var lote = Lote(status);
        var cerrada = Parte(lote.Id, 1, ConsolidadoExportPartStatus.Cerrada);
        var pendiente = Parte(lote.Id, 2, ConsolidadoExportPartStatus.Pendiente);
        var fallida = Parte(lote.Id, 3, ConsolidadoExportPartStatus.Fallida);

        ConsolidadoLotePurga.EsPurgable(lote).Should().BeTrue();
        ConsolidadoLotePurga.Aplicar(lote, [cerrada, pendiente, fallida], Ahora);

        lote.DekWrapped.Should().BeNull("borrado criptográfico");
        lote.Status.Should().Be(ConsolidadoExportStatus.Expirado);
        lote.PurgedAt.Should().Be(Ahora);
        lote.ExpiresAt.Should().Be(Ahora.AddHours(22), "la fecha de expiración original se conserva");
        cerrada.Status.Should().Be(ConsolidadoExportPartStatus.Purgada);
        cerrada.PurgedAt.Should().Be(Ahora);
        pendiente.Status.Should().Be(ConsolidadoExportPartStatus.Descartada);
        fallida.Status.Should().Be(ConsolidadoExportPartStatus.Fallida);
        ConsolidadoLotePurga.EsPurgable(lote).Should().BeFalse("ya purgado");
    }

    [Theory]
    [InlineData(ConsolidadoExportStatus.EnCola)]
    [InlineData(ConsolidadoExportStatus.EnProceso)]
    [InlineData(ConsolidadoExportStatus.Empaquetando)]
    public void LoteActivo_NoSePurga(string status)
    {
        var lote = Lote(status);

        ConsolidadoLotePurga.EsPurgable(lote).Should().BeFalse();
        var act = () => ConsolidadoLotePurga.Aplicar(lote, [], Ahora);
        act.Should().Throw<InvalidOperationException>();
        lote.DekWrapped.Should().NotBeNull();
    }

    [Fact]
    public void LoteCanceladoYaPurgado_NoSeVuelveAPurgar()
    {
        var lote = Lote(ConsolidadoExportStatus.Cancelado);
        lote.PurgedAt = Ahora.AddHours(-1);
        lote.DekWrapped = null;

        ConsolidadoLotePurga.EsPurgable(lote).Should().BeFalse();
    }

    [Fact]
    public void ParteDeOtroLote_SeRechaza()
    {
        var lote = Lote(ConsolidadoExportStatus.Completado);

        var act = () => ConsolidadoLotePurga.Aplicar(lote, [Parte(Guid.NewGuid(), 1, ConsolidadoExportPartStatus.Cerrada)], Ahora);

        act.Should().Throw<InvalidOperationException>();
    }
}
