using Flit.Tramites.Application.UseCases.ConsolidadoLotes;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Flit.Infrastructure.Messaging;

/// <summary>
/// HU #13378 AC5 (ADR-0070 D4) — al arrancar core-api borra los temporales huérfanos del carril de empaquetado
/// (<see cref="ConsolidadoLoteTemporales.LimpiarHuerfanos"/>): ZIP en claro y partes cifradas de una ejecución que murió
/// (caída, <c>docker kill</c>). Corre en <see cref="StartAsync"/>, antes de que el carril reclame ninguna parte (se
/// registra antes que <see cref="ConsolidadoLoteProcessor"/>, que además espera su retraso inicial). El directorio es
/// local al contenedor: otra réplica no lo comparte. Un fallo de E/S solo se registra: no impide el arranque.
/// </summary>
/// <remarks>Uso de ejemplo: <c>services.AddHostedService&lt;ConsolidadoLoteTemporalesLimpieza&gt;();</c>.</remarks>
internal sealed partial class ConsolidadoLoteTemporalesLimpieza(
    ConsolidadoLoteTemporales temporales, ILogger<ConsolidadoLoteTemporalesLimpieza> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            var borrados = temporales.LimpiarHuerfanos();
            if (borrados > 0)
                LogLimpieza(logger, borrados);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogLimpiezaFallida(logger, ex.GetType().Name);
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Carril de empaquetado de lotes: {Borrados} temporal(es) huérfano(s) borrado(s) al arrancar.")]
    private static partial void LogLimpieza(ILogger logger, int borrados);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Carril de empaquetado de lotes: no se pudieron limpiar los temporales al arrancar ({ExceptionType}).")]
    private static partial void LogLimpiezaFallida(ILogger logger, string exceptionType);
}
