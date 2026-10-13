using System.Net;
using Flit.Tramites.Application.UseCases.ConsolidadoLotes;
using Flit.Tramites.Domain.Entities.ConsolidadoLotes;
using Flit.Tramites.Domain.Repositories;
using FluentAssertions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Flit.Tramites.Application.Tests.ConsolidadoLotes;

/// <summary>
/// HU #13385 (Feature #13307, diseño 09 §2.4) — <see cref="CancelarLoteConsolidadosHandler"/> sobre un repositorio doble:
/// traduce el resultado de la transacción, pasa el dueño (<c>sub</c>) y los datos de auditoría tal cual, y borra los
/// binarios de las partes purgadas best-effort DESPUÉS de confirmar (paso 6). La transacción real la cubre
/// <c>ConsolidadoLoteRepositoryCancelTests</c> (PostgreSQL).
/// </summary>
/// <remarks>
/// Uso de ejemplo:
/// <code>
/// var r = await new CancelarLoteConsolidadosHandler(repo, storage).HandleAsync(new(loteId, sub, "Radicador"), ct);
/// r.Aceptado.Should().BeTrue();
/// </code>
/// </remarks>
public sealed class CancelarLoteConsolidadosHandlerTests
{
    private static readonly Guid LoteId = Guid.Parse("13385000-0000-4000-8000-0000000000a1");
    private static readonly Guid Sub = Guid.Parse("13385000-0000-4000-8000-0000000000b1");

    private readonly IConsolidadoLoteRepository _repo = Substitute.For<IConsolidadoLoteRepository>();
    private readonly IConsolidadoLoteParteStorage _storage = Substitute.For<IConsolidadoLoteParteStorage>();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private CancelarLoteConsolidadosHandler Handler() => new(_repo, _storage);

    private static ConsolidadoExportBatch Lote(string estado) => new()
    {
        Id = LoteId,
        RequestedByUserId = Sub,
        Status = estado,
        TotalItems = 10,
        IncludedCount = 4,
        OmittedCount = 1,
        GeneratedCount = 3,
    };

    private void Responde(CancelarLoteResultado r) =>
        _repo.CancelarAsync(Arg.Any<CancelacionLote>(), Arg.Any<CancellationToken>()).Returns(r);

    [Fact]
    public async Task AC1_Cancelado_Aceptado_PasaSubRolIpYNavegador_YBorraLasPartesPurgadasDespues()
    {
        var lote = Lote(ConsolidadoExportStatus.Cancelado);
        Responde(new CancelarLoteResultado(CancelarLoteEstado.Cancelado, lote, 7, ["fm/parte-1", "fm/parte-2"]));
        var ip = IPAddress.Parse("10.13.38.5");

        var r = await Handler().HandleAsync(new CancelarLoteConsolidadosCommand(LoteId, Sub, "Radicador", ip, "ua/13385"), Ct);

        r.Aceptado.Should().BeTrue();
        r.Estado.Should().Be(CancelarLoteEstado.Cancelado);
        r.Lote.Should().BeSameAs(lote);
        await _repo.Received(1).CancelarAsync(
            new CancelacionLote(LoteId, Sub, "Radicador", ip, "ua/13385"), Arg.Any<CancellationToken>());
        Received.InOrder(() =>
        {
            _repo.CancelarAsync(Arg.Any<CancelacionLote>(), Arg.Any<CancellationToken>());
            _storage.Delete("fm/parte-1");
            _storage.Delete("fm/parte-2");
        });
    }

    [Fact]
    public async Task AC1_SiElBorradoBestEffortFalla_LaCancelacionSigueAceptada()
    {
        Responde(new CancelarLoteResultado(CancelarLoteEstado.Cancelado, Lote(ConsolidadoExportStatus.Cancelado), 1, ["fm/a", "fm/b"]));
        _storage.When(s => s.Delete("fm/a")).Throw(new IOException("file-manager caído"));

        var r = await Handler().HandleAsync(new CancelarLoteConsolidadosCommand(LoteId, Sub, "Radicador"), Ct);

        r.Aceptado.Should().BeTrue("la cancelación ya está confirmada; el binario es ilegible sin la DEK");
        _storage.Received(1).Delete("fm/b");
    }

    [Fact]
    public async Task AC4_YaCancelado_Aceptado_SinBorrarNada()
    {
        var lote = Lote(ConsolidadoExportStatus.Cancelado);
        Responde(new CancelarLoteResultado(CancelarLoteEstado.YaCancelado, lote));

        var r = await Handler().HandleAsync(new CancelarLoteConsolidadosCommand(LoteId, Sub, "Radicador"), Ct);

        r.Aceptado.Should().BeTrue();
        r.Lote.Should().BeSameAs(lote);
        _storage.DidNotReceiveWithAnyArgs().Delete(default!);
    }

    [Theory]
    [InlineData(CancelarLoteEstado.NoEncontrado)]
    [InlineData(CancelarLoteEstado.Terminado)]
    [InlineData(CancelarLoteEstado.NoRegistrado)]
    public async Task AC5_AC6_AC8_NoAceptado_SeTraduceTalCual_YNoBorraNada(CancelarLoteEstado estado)
    {
        var lote = estado == CancelarLoteEstado.Terminado ? Lote(ConsolidadoExportStatus.Completado) : null;
        Responde(new CancelarLoteResultado(estado, lote));

        var r = await Handler().HandleAsync(new CancelarLoteConsolidadosCommand(LoteId, Sub, "Radicador"), Ct);

        r.Aceptado.Should().BeFalse();
        r.Estado.Should().Be(estado);
        r.Lote.Should().BeSameAs(lote);
        _storage.DidNotReceiveWithAnyArgs().Delete(default!);
    }

    [Fact]
    public async Task AC6_IdOSubVacios_NoEncontrado_SinTocarElRepositorio()
    {
        (await Handler().HandleAsync(new CancelarLoteConsolidadosCommand(Guid.Empty, Sub, "Radicador"), Ct))
            .Estado.Should().Be(CancelarLoteEstado.NoEncontrado);
        (await Handler().HandleAsync(new CancelarLoteConsolidadosCommand(LoteId, Guid.Empty, "Radicador"), Ct))
            .Estado.Should().Be(CancelarLoteEstado.NoEncontrado);

        await _repo.DidNotReceiveWithAnyArgs().CancelarAsync(default!, Ct);
    }

    [Fact]
    public async Task Contrato_ComandoNulo_Lanza()
    {
        var act = () => Handler().HandleAsync(null!, Ct);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }
}

/// <summary>
/// HU #13385 — operación de dominio <see cref="ConsolidadoLoteCancelacion"/> (diseño 09 §2.4 pasos 3–4): reutiliza el
/// descarte de partes de <see cref="ConsolidadoLotePurga"/>.
/// </summary>
/// <remarks>Uso de ejemplo: <c>ConsolidadoLoteCancelacion.Aplicar(lote, partes, ahora, sub)</c> ⇒ rutas purgadas.</remarks>
public sealed class ConsolidadoLoteCancelacionTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 10, 7, 21, 0, 0, TimeSpan.Zero);
    private static readonly Guid Sub = Guid.Parse("13385000-0000-4000-8000-0000000000b1");

    private static ConsolidadoExportBatch Lote(string status) => new()
    {
        Id = Guid.NewGuid(),
        Status = status,
        DekWrapped = [1, 2, 3],
        TotalItems = 10,
        IncludedCount = 4,
        OmittedCount = 1,
        GeneratedCount = 3,
        FinishedAt = ConsolidadoExportStatus.EsActivo(status) ? null : Ahora.AddHours(-2),
        ExpiresAt = ConsolidadoExportStatus.EsActivo(status) ? null : Ahora.AddHours(22),
    };

    private static ConsolidadoExportBatchPart Parte(Guid lote, short n, string status, string? ruta = null) => new()
    {
        Id = Guid.NewGuid(),
        BatchId = lote,
        PartNumber = n,
        Status = status,
        StoragePath = ruta,
        LeaseUntil = status == ConsolidadoExportPartStatus.Empaquetando ? Ahora.AddMinutes(5) : null,
    };

    [Theory]
    [InlineData(ConsolidadoExportStatus.EnCola)]
    [InlineData(ConsolidadoExportStatus.EnProceso)]
    [InlineData(ConsolidadoExportStatus.Empaquetando)]
    public void AC1_LoteActivo_QuedaCancelado_SinDek_InstantesIguales_ContadoresIntactos_YPartesDescartadas(string status)
    {
        var lote = Lote(status);
        var cerrada = Parte(lote.Id, 1, ConsolidadoExportPartStatus.Cerrada, "fm/parte-1");
        var empaquetando = Parte(lote.Id, 2, ConsolidadoExportPartStatus.Empaquetando);
        var pendiente = Parte(lote.Id, 3, ConsolidadoExportPartStatus.Pendiente);
        var fallida = Parte(lote.Id, 4, ConsolidadoExportPartStatus.Fallida);

        ConsolidadoLoteCancelacion.EsCancelable(lote).Should().BeTrue();
        var rutas = ConsolidadoLoteCancelacion.Aplicar(lote, [cerrada, empaquetando, pendiente, fallida], Ahora, Sub);

        lote.Status.Should().Be(ConsolidadoExportStatus.Cancelado);
        lote.DekWrapped.Should().BeNull("borrado criptográfico");
        lote.FinishedAt.Should().Be(Ahora);
        lote.ExpiresAt.Should().Be(Ahora);
        lote.PurgedAt.Should().Be(Ahora);
        lote.UpdatedBy.Should().Be(Sub);
        (lote.TotalItems, lote.IncludedCount, lote.OmittedCount, lote.GeneratedCount).Should().Be((10, 4, 1, 3));
        cerrada.Status.Should().Be(ConsolidadoExportPartStatus.Purgada);
        cerrada.PurgedAt.Should().Be(Ahora);
        empaquetando.Status.Should().Be(ConsolidadoExportPartStatus.Descartada);
        empaquetando.LeaseUntil.Should().BeNull();
        pendiente.Status.Should().Be(ConsolidadoExportPartStatus.Descartada);
        fallida.Status.Should().Be(ConsolidadoExportPartStatus.Fallida);
        rutas.Should().Equal("fm/parte-1");
        ConsolidadoLoteCancelacion.EsCancelable(lote).Should().BeFalse("ya terminal");
        ConsolidadoLotePurga.EsPurgable(lote).Should().BeFalse("ya purgado: la purga no lo pasa a expirado");
    }

    [Theory]
    [InlineData(ConsolidadoExportStatus.Completado)]
    [InlineData(ConsolidadoExportStatus.CompletadoConOmitidos)]
    [InlineData(ConsolidadoExportStatus.Fallido)]
    [InlineData(ConsolidadoExportStatus.Expirado)]
    [InlineData(ConsolidadoExportStatus.Cancelado)]
    public void AC5_LoteTerminal_NoEsCancelable_YAplicarLanzaSinTocarlo(string status)
    {
        var lote = Lote(status);
        var cerrada = Parte(lote.Id, 1, ConsolidadoExportPartStatus.Cerrada, "fm/x");

        ConsolidadoLoteCancelacion.EsCancelable(lote).Should().BeFalse();
        var act = () => ConsolidadoLoteCancelacion.Aplicar(lote, [cerrada], Ahora, Sub);

        act.Should().Throw<InvalidOperationException>();
        lote.Status.Should().Be(status);
        lote.DekWrapped.Should().NotBeNull();
        cerrada.Status.Should().Be(ConsolidadoExportPartStatus.Cerrada);
    }

    [Fact]
    public void Contrato_LoteBorrado_NoEsCancelable_YParteAjena_Lanza()
    {
        var borrado = Lote(ConsolidadoExportStatus.EnProceso);
        borrado.DeletedAt = Ahora;
        ConsolidadoLoteCancelacion.EsCancelable(borrado).Should().BeFalse();

        var lote = Lote(ConsolidadoExportStatus.EnProceso);
        var act = () => ConsolidadoLoteCancelacion.Aplicar(
            lote, [Parte(Guid.NewGuid(), 1, ConsolidadoExportPartStatus.Pendiente)], Ahora, Sub);
        act.Should().Throw<InvalidOperationException>();
    }
}
