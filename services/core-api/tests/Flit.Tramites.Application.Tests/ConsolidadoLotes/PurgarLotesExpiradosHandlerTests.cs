using Flit.Tramites.Application.UseCases.ConsolidadoLotes;
using Flit.Tramites.Domain.Repositories;
using FluentAssertions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Flit.Tramites.Application.Tests.ConsolidadoLotes;

/// <summary>
/// HU #13379 (Épica #13216, AC5) — <see cref="PurgarLotesExpiradosHandler"/>: consulta los vencidos con el mismo «ahora»
/// y purga cada uno con <see cref="IConsolidadoLoteRepository.PurgarAsync"/> (DEK a NULL, partes purgada, lote expirado,
/// <c>lote_purgado</c> en la misma transacción); un fallo en un lote no frena a los demás.
/// <para>Uso de ejemplo: <c>var r = await handler.HandleAsync(ct); r.Purgados</c>.</para>
/// </summary>
public sealed class PurgarLotesExpiradosHandlerTests
{
    private readonly IConsolidadoLoteLectura _lectura = Substitute.For<IConsolidadoLoteLectura>();
    private readonly IConsolidadoLoteRepository _repo = Substitute.For<IConsolidadoLoteRepository>();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private PurgarLotesExpiradosHandler Handler() => new(_lectura, _repo, reloj: new RelojFijo(LoteConsulta.Ahora));

    [Fact]
    public async Task AC5_PurgaCadaLoteVencido_ConElMismoInstante()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        _lectura.ObtenerVencidosAsync(LoteConsulta.Ahora, PurgarLotesExpiradosHandler.MaxPorCiclo, Arg.Any<CancellationToken>())
            .Returns([a, b]);
        _repo.PurgarAsync(Arg.Any<Guid>(), LoteConsulta.Ahora, Arg.Any<CancellationToken>()).Returns(true);

        var r = await Handler().HandleAsync(Ct);

        r.Should().Be(new PurgaLotesResultado(2, 2, 0));
        Received.InOrder(() =>
        {
            _repo.PurgarAsync(a, LoteConsulta.Ahora, Arg.Any<CancellationToken>());
            _repo.PurgarAsync(b, LoteConsulta.Ahora, Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public async Task AC5_SinVencidos_NoPurgaNada()
    {
        _lectura.ObtenerVencidosAsync(Arg.Any<DateTimeOffset>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns([]);

        (await Handler().HandleAsync(Ct)).Should().Be(new PurgaLotesResultado(0, 0, 0));
        await _repo.DidNotReceiveWithAnyArgs().PurgarAsync(default, default, Ct);
    }

    [Fact]
    public async Task AC5_Borde_UnLoteQueFalla_NoFrenaALosDemas_YElYaPurgadoNoCuenta()
    {
        var falla = Guid.NewGuid();
        var yaPurgado = Guid.NewGuid();
        var ok = Guid.NewGuid();
        _lectura.ObtenerVencidosAsync(Arg.Any<DateTimeOffset>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([falla, yaPurgado, ok]);
        _repo.PurgarAsync(falla, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).ThrowsAsync(new TimeoutException());
        _repo.PurgarAsync(yaPurgado, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(false);
        _repo.PurgarAsync(ok, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(true);

        var r = await Handler().HandleAsync(Ct);

        r.Should().Be(new PurgaLotesResultado(3, 1, 1));
    }

    [Fact]
    public async Task AC5_Contrato_LaCancelacionSePropaga()
    {
        var id = Guid.NewGuid();
        _lectura.ObtenerVencidosAsync(Arg.Any<DateTimeOffset>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns([id]);
        _repo.PurgarAsync(id, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).ThrowsAsync(new OperationCanceledException());

        var act = () => Handler().HandleAsync(Ct);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
