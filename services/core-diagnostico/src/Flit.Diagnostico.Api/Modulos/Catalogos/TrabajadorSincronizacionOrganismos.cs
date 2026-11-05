namespace Flit.Diagnostico.Api.Modulos.Catalogos;

/// <summary>
/// Sincroniza la réplica de organismos al arrancar y luego cada <see cref="ClaveIntervalo"/> (por defecto 24 horas, como la
/// réplica diaria de Comparendos, HU #13459). Solo se registra con <c>Diagnostico:Tramites:Address</c>; una falla del gRPC se
/// registra y se reintenta en la siguiente vuelta, sin tumbar el servicio. No mira <c>IHostEnvironment</c>.
/// </summary>
/// <example>
/// <code>
/// Diagnostico__Organismos__IntervaloSincronizacion=1.00:00:00
/// </code>
/// </example>
internal sealed partial class TrabajadorSincronizacionOrganismos(
    IServiceScopeFactory scopes, IConfiguration configuracion, TimeProvider reloj, ILogger<TrabajadorSincronizacionOrganismos> log) : BackgroundService
{
    public const string ClaveIntervalo = "Diagnostico:Organismos:IntervaloSincronizacion";

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
                await scope.ServiceProvider.GetRequiredService<SincronizacionOrganismos>().SincronizarAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
#pragma warning disable CA1031 // Una falla del catálogo no tumba el servicio: se reintenta en la siguiente vuelta.
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

    [LoggerMessage(EventId = 137333, Level = LogLevel.Warning, Message = "La sincronización de organismos falló ({TipoError}); se reintenta en la siguiente vuelta")]
    private static partial void SincronizacionFallida(ILogger logger, string tipoError);
}
