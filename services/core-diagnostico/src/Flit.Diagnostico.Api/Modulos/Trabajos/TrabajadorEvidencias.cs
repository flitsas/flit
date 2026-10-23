using Flit.Diagnostico.Api.Modulos.Reportes;
using Microsoft.Extensions.Options;

namespace Flit.Diagnostico.Api.Modulos.Trabajos;

/// <summary>
/// Trabajador en segundo plano del ZIP del lote (HU #13850, decisión Q2): da vueltas de <see cref="EmpaquetadorZip"/> mientras haya
/// ZIP en la cola y espera <see cref="OpcionesEvidencias.Intervalo"/> (con el reloj inyectado) cuando está vacía. Solo se registra con
/// <c>Diagnostico:Evidencias:Habilitado=true</c> (nunca por el entorno del host). Cada réplica corre el suyo: la cola evita la doble
/// toma. Al detenerse cancela el ZIP en curso, que se retoma al vencer su lease.
/// </summary>
internal sealed partial class TrabajadorEvidencias(
    EmpaquetadorZip empaquetador, IOptions<OpcionesEvidencias> opciones, TimeProvider reloj, ILogger<TrabajadorEvidencias> log) : BackgroundService
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
                tomo = await empaquetador.ProcesarSiguienteAsync(stoppingToken).ConfigureAwait(false);
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

    [LoggerMessage(EventId = 138520, Level = LogLevel.Information, Message = "Trabajador del ZIP del lote en marcha: lease {DuracionLease}, {MaxReintentos} reintentos")]
    private static partial void Arranca(ILogger logger, TimeSpan duracionLease, int maxReintentos);

    [LoggerMessage(EventId = 138521, Level = LogLevel.Error, Message = "Falló una vuelta del trabajador del ZIP; se reintenta tras el intervalo")]
    private static partial void VueltaFallida(ILogger logger, Exception ex);
}
