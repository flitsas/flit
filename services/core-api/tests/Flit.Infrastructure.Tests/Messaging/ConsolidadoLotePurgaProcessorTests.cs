using Flit.Infrastructure.Messaging;
using Flit.Tramites.Application.UseCases.ConsolidadoLotes;
using Flit.Tramites.Domain.Entities.ConsolidadoLotes;
using Flit.Tramites.Domain.Repositories;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Flit.Infrastructure.Tests.Messaging;

/// <summary>
/// HU #13379 (Épica #13216, AC5) — <see cref="ConsolidadoLotePurgaProcessor"/>: cada 10 min purga los lotes vencidos con
/// <see cref="PurgarLotesExpiradosHandler"/> (real) sobre dobles de lectura y repositorio. Corre aunque el motor esté
/// apagado (no lee <c>consolidado_export_settings</c>), repite si el ciclo sale lleno y un ciclo fallido no lo tumba.
/// <para>Uso de ejemplo: <c>await processor.PurgarAsync(ct)</c> ejecuta un ciclo; <c>StartAsync</c> lo repite cada periodo.</para>
/// </summary>
public sealed class ConsolidadoLotePurgaProcessorTests
{
    private readonly IConsolidadoLoteLectura _lectura = Substitute.For<IConsolidadoLoteLectura>();
    private readonly IConsolidadoLoteRepository _repo = Substitute.For<IConsolidadoLoteRepository>();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private ConsolidadoLotePurgaProcessor Crear(TimeSpan? periodo = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped(_ => _lectura);
        services.AddScoped(_ => _repo);
        services.AddScoped<PurgarLotesExpiradosHandler>();
        var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        return new ConsolidadoLotePurgaProcessor(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<ConsolidadoLotePurgaProcessor>.Instance,
            new ConsolidadoLotePurgaOptions(periodo ?? TimeSpan.FromMinutes(10), TimeSpan.Zero),
            TimeProvider.System);
    }

    [Fact]
    public void AC5_Contrato_ElCarrilCorreCadaDiezMinutos()
    {
        ConsolidadoLotePurgaOptions.Predeterminadas.Periodo.Should().Be(TimeSpan.FromMinutes(10));
    }

    [Fact]
    public async Task AC5_ConElMotorApagado_PurgaIgual_YNoLeeLosParametrosDelMotor()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        _repo.ObtenerSettingsAsync(Arg.Any<CancellationToken>()).Returns(new ConsolidadoExportSettings { IsActive = false });
        _lectura.ObtenerVencidosAsync(Arg.Any<DateTimeOffset>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns([a, b]);
        _repo.PurgarAsync(Arg.Any<Guid>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(true);

        await Crear().PurgarAsync(Ct);

        await _repo.Received(1).PurgarAsync(a, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
        await _repo.Received(1).PurgarAsync(b, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
        await _repo.DidNotReceiveWithAnyArgs().ObtenerSettingsAsync(Ct);
    }

    [Fact]
    public async Task AC5_CicloLleno_RepiteHastaVaciar()
    {
        var lleno = Enumerable.Range(0, PurgarLotesExpiradosHandler.MaxPorCiclo).Select(_ => Guid.NewGuid()).ToArray();
        var resto = new[] { Guid.NewGuid() };
        _lectura.ObtenerVencidosAsync(Arg.Any<DateTimeOffset>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(lleno, resto);
        _repo.PurgarAsync(Arg.Any<Guid>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(true);

        await Crear().PurgarAsync(Ct);

        await _lectura.Received(2).ObtenerVencidosAsync(Arg.Any<DateTimeOffset>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
        await _repo.Received(PurgarLotesExpiradosHandler.MaxPorCiclo + 1)
            .PurgarAsync(Arg.Any<Guid>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AC5_Borde_UnCicloFallido_NoLanza()
    {
        _lectura.ObtenerVencidosAsync(Arg.Any<DateTimeOffset>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("bd caida"));

        var act = () => Crear().PurgarAsync(Ct);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task AC5_ElServicioAlojado_EjecutaLaPurgaPeriodicamente()
    {
        var id = Guid.NewGuid();
        var llamadas = 0;
        _lectura.ObtenerVencidosAsync(Arg.Any<DateTimeOffset>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                Interlocked.Increment(ref llamadas);
                return (IReadOnlyList<Guid>)[id];
            });
        _repo.PurgarAsync(id, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(true);
        var processor = Crear(TimeSpan.FromMilliseconds(30));

        await processor.StartAsync(Ct);
        var limite = DateTime.UtcNow.AddSeconds(10);
        while (Volatile.Read(ref llamadas) < 2 && DateTime.UtcNow < limite)
            await Task.Delay(20, Ct);
        await processor.StopAsync(Ct);

        Volatile.Read(ref llamadas).Should().BeGreaterThanOrEqualTo(2, "un ciclo por periodo");
        await _repo.Received().PurgarAsync(id, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }
}
