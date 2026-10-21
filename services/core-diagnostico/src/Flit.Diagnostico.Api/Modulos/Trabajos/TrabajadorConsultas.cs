using Microsoft.Extensions.Options;

namespace Flit.Diagnostico.Api.Modulos.Trabajos;

/// <summary>
/// Trabajador en segundo plano del motor de consultas (HU #13727): da vueltas de <see cref="ProcesadorConsultas"/> mientras
/// haya trabajo y espera <see cref="OpcionesProcesador.Intervalo"/> cuando la cola está vacía. Solo se registra con
/// <c>Diagnostico:Procesador:Habilitado=true</c> (nunca por el entorno del host). Cada réplica corre el suyo: la cola evita
/// la doble toma. Al detenerse cancela la vuelta en curso y sus celdas se retoman al vencer el lease.
/// </summary>
internal sealed partial class TrabajadorConsultas(
    ProcesadorConsultas procesador, IOptions<OpcionesProcesador> opciones, TimeProvider reloj, ILogger<TrabajadorConsultas> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var o = opciones.Value;
        Arranca(log, o.TamanoLote, o.DuracionLease, o.VehiculosEnVueloPorEmpresa);
        while (!stoppingToken.IsCancellationRequested)
        {
            var tomadas = 0;
            try
            {
                tomadas = await procesador.ProcesarLoteAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
#pragma warning disable CA1031 // Una vuelta fallida (base caída) no detiene el trabajador: se reintenta tras el intervalo.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                VueltaFallida(log, ex);
            }

            if (tomadas > 0)
                continue;
            try
            {
                await Task.Delay(o.Intervalo, reloj, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    [LoggerMessage(EventId = 137272, Level = LogLevel.Information,
        Message = "Procesador de consultas en marcha: lote {TamanoLote}, lease {DuracionLease}, {VehiculosEnVuelo} vehículos en vuelo por empresa")]
    private static partial void Arranca(ILogger logger, int tamanoLote, TimeSpan duracionLease, int vehiculosEnVuelo);

    [LoggerMessage(EventId = 137273, Level = LogLevel.Error, Message = "Falló una vuelta del procesador de consultas; se reintenta tras el intervalo")]
    private static partial void VueltaFallida(ILogger logger, Exception ex);
}
