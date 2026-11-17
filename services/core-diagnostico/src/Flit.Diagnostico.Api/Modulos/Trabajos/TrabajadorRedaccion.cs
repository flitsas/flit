using Microsoft.Extensions.Options;

namespace Flit.Diagnostico.Api.Modulos.Trabajos;

/// <summary>
/// Trabajador en segundo plano de la redacción (corte 6): da vueltas de <see cref="RedactorEnCola"/> mientras haya dictámenes
/// tomables y espera <see cref="OpcionesRedaccion.Intervalo"/> (con el reloj inyectado) cuando la cola está vacía. Solo se
/// registra con <c>Diagnostico:Redaccion:Habilitado=true</c> (nunca por el entorno del host). Cada réplica corre el suyo: la
/// cola evita la doble toma. Al detenerse cancela la vuelta en curso; la fila se retoma al vencer su lease.
/// </summary>
internal sealed partial class TrabajadorRedaccion(
    RedactorEnCola redactor, IOptions<OpcionesRedaccion> opciones, TimeProvider reloj, ILogger<TrabajadorRedaccion> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var o = opciones.Value;
        Arranca(log, o.DuracionLease, o.MaxReintentos);
        while (!stoppingToken.IsCancellationRequested)
        {
            var tomo = false;
            try
            {
                tomo = await redactor.ProcesarSiguienteAsync(stoppingToken).ConfigureAwait(false);
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

            if (tomo)
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

    [LoggerMessage(EventId = 126788, Level = LogLevel.Information, Message = "Trabajador de redacción en marcha: lease {DuracionLease}, {MaxReintentos} reintentos")]
    private static partial void Arranca(ILogger logger, TimeSpan duracionLease, int maxReintentos);

    [LoggerMessage(EventId = 126789, Level = LogLevel.Error, Message = "Falló una vuelta del trabajador de redacción; se reintenta tras el intervalo")]
    private static partial void VueltaFallida(ILogger logger, Exception ex);
}
