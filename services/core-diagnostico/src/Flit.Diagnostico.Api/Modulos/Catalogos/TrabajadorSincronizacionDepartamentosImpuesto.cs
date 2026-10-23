namespace Flit.Diagnostico.Api.Modulos.Catalogos;

/// <summary>
/// Sincroniza la réplica de departamentos con impuesto (HU #13861) al arrancar y luego cada <see cref="ClaveIntervalo"/> (por
/// defecto 24 horas; un valor cero o negativo usa el de defecto), con <c>Task.Delay(intervalo, reloj, ct)</c> sobre el
/// <see cref="TimeProvider"/> inyectado. Se registra siempre: con la capacidad no conectada cada vuelta solo carga
/// <see cref="EstadoCapacidadImpuesto"/> desde la base. Una excepción se registra (EventId <see cref="EventoSincronizacionFallida"/>)
/// y se reintenta en la siguiente vuelta, sin tumbar el servicio. No mira <c>IHostEnvironment</c>.
/// </summary>
/// <example>
/// <code>
/// Diagnostico__Impuesto__IntervaloSincronizacion=1.00:00:00
/// </code>
/// </example>
internal sealed partial class TrabajadorSincronizacionDepartamentosImpuesto(
    IServiceScopeFactory scopes, IConfiguration configuracion, TimeProvider reloj, ILogger<TrabajadorSincronizacionDepartamentosImpuesto> log) : BackgroundService
{
    public const string ClaveIntervalo = "Diagnostico:Impuesto:IntervaloSincronizacion";

    public const int EventoSincronizacionFallida = 138613;

    private static readonly TimeSpan IntervaloPorDefecto = TimeSpan.FromHours(24);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var intervalo = configuracion.GetValue(ClaveIntervalo, IntervaloPorDefecto);
        if (intervalo <= TimeSpan.Zero)
            intervalo = IntervaloPorDefecto;
        do
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<ISincronizacionDepartamentosImpuesto>().SincronizarAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
#pragma warning disable CA1031 // Una falla de la capacidad o de la base no tumba el servicio: se reintenta en la siguiente vuelta.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                SincronizacionFallida(log, ex.GetType().Name);
            }
        }
        while (await Esperar(intervalo, stoppingToken).ConfigureAwait(false));
    }

    private async Task<bool> Esperar(TimeSpan intervalo, CancellationToken ct)
    {
        try
        {
            await Task.Delay(intervalo, reloj, ct).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    [LoggerMessage(EventId = EventoSincronizacionFallida, Level = LogLevel.Warning, Message = "La sincronización de departamentos con impuesto falló ({TipoError}); se reintenta en la siguiente vuelta")]
    private static partial void SincronizacionFallida(ILogger logger, string tipoError);
}
