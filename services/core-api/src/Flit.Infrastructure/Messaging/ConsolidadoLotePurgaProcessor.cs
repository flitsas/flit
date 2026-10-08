using Flit.Tramites.Application.UseCases.ConsolidadoLotes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Flit.Infrastructure.Messaging;

/// <summary>Ritmo del carril de purga.</summary>
/// <param name="Periodo">Espera entre ciclos (10 min en producción).</param>
/// <param name="RetrasoInicial">Espera al arrancar el host (deja terminar migraciones y seeds).</param>
internal sealed record ConsolidadoLotePurgaOptions(TimeSpan Periodo, TimeSpan RetrasoInicial)
{
    public static ConsolidadoLotePurgaOptions Predeterminadas { get; } =
        new(TimeSpan.FromMinutes(10), TimeSpan.FromSeconds(30));
}

/// <summary>
/// HU #13379 (Épica #13216, ADR-0070 D4, CF-12) — carril de purga a las 24 h de los lotes de descarga masiva de
/// consolidados. Mismo patrón que <see cref="ConsolidadoLoteProcessor"/> (bucle con retraso inicial, scope de DI por
/// ciclo, nunca muere por un ciclo fallido, logs sin PII) con un ciclo cada <see cref="ConsolidadoLotePurgaOptions.Periodo"/>
/// (10 min) que delega en <see cref="PurgarLotesExpiradosHandler"/>; si un ciclo sale lleno
/// (<see cref="PurgarLotesExpiradosHandler.MaxPorCiclo"/>) repite enseguida, hasta <see cref="MaxCiclosSeguidos"/>.
/// <para><b>Corre aunque el motor esté apagado</b> (<c>consolidado_export_settings.is_active = false</c> o sin fila), a
/// diferencia de los carriles de ítems y empaquetado: el interruptor detiene el TRABAJO del motor (reclamar, generar,
/// crear lotes, decisión S4), pero la retención de 24 h es una garantía de privacidad (CF-12, Ley 1581, borrado
/// criptográfico). Apagar el motor para una contingencia no debe dejar DEK vivas más allá del plazo prometido. Por eso
/// este carril no lee los parámetros del motor.</para>
/// La descarga no depende de este carril para negar un lote vencido: <see cref="ConsolidadoLoteDescargabilidad"/> ya da
/// 410 con <c>expires_at &lt;= now</c> aunque la purga no haya pasado todavía.
/// </summary>
/// <remarks>Uso de ejemplo: <c>services.AddHostedService&lt;ConsolidadoLotePurgaProcessor&gt;();</c> (<c>InfrastructureExtensions</c>).</remarks>
internal sealed partial class ConsolidadoLotePurgaProcessor : BackgroundService
{
    /// <summary>Ciclos seguidos como mucho cuando salen llenos (100 × 10 = 1.000 lotes) antes de esperar el periodo.</summary>
    internal const int MaxCiclosSeguidos = 10;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ConsolidadoLotePurgaProcessor> _logger;
    private readonly ConsolidadoLotePurgaOptions _opciones;
    private readonly TimeProvider _time;

    public ConsolidadoLotePurgaProcessor(IServiceScopeFactory scopeFactory, ILogger<ConsolidadoLotePurgaProcessor> logger)
        : this(scopeFactory, logger, ConsolidadoLotePurgaOptions.Predeterminadas, TimeProvider.System)
    {
    }

    internal ConsolidadoLotePurgaProcessor(
        IServiceScopeFactory scopeFactory,
        ILogger<ConsolidadoLotePurgaProcessor> logger,
        ConsolidadoLotePurgaOptions opciones,
        TimeProvider timeProvider)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _opciones = opciones ?? throw new ArgumentNullException(nameof(opciones));
        _time = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            if (_opciones.RetrasoInicial > TimeSpan.Zero)
                await Task.Delay(_opciones.RetrasoInicial, _time, stoppingToken).ConfigureAwait(false);

            while (!stoppingToken.IsCancellationRequested)
            {
                await PurgarAsync(stoppingToken).ConfigureAwait(false);
                await Task.Delay(_opciones.Periodo, _time, stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Parada del host: la purga pendiente la retoma el arranque siguiente.
        }
    }

    /// <summary>Un ciclo (con repeticiones si sale lleno). Nunca lanza salvo la cancelación del host.</summary>
    internal async Task PurgarAsync(CancellationToken stoppingToken)
    {
        for (var ciclo = 0; ciclo < MaxCiclosSeguidos; ciclo++)
        {
            PurgaLotesResultado resultado;
            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                var handler = scope.ServiceProvider.GetRequiredService<PurgarLotesExpiradosHandler>();
                resultado = await handler.HandleAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                throw;
            }
#pragma warning disable CA1031 // El carril no puede morir por un ciclo: se registra y se reintenta en el periodo siguiente.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                // Solo el tipo: el mensaje de una excepción de BD puede citar valores de la fila.
                LogCicloFallido(_logger, ex.GetType().Name);
                return;
            }

            if (resultado.Vencidos > 0)
                LogCiclo(_logger, resultado.Vencidos, resultado.Purgados, resultado.Fallidos);
            if (resultado.Vencidos < PurgarLotesExpiradosHandler.MaxPorCiclo)
                return;
        }
    }

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Carril de purga de lotes: {Vencidos} vencido(s), {Purgados} purgado(s), {Fallidos} con fallo (se reintentan).")]
    private static partial void LogCiclo(ILogger logger, int vencidos, int purgados, int fallidos);

    [LoggerMessage(Level = LogLevel.Error, Message = "Carril de purga de lotes: falló un ciclo ({ExceptionType}); se reintentará.")]
    private static partial void LogCicloFallido(ILogger logger, string exceptionType);
}
