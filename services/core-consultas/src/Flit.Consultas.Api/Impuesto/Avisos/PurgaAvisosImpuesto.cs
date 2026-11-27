namespace Flit.Consultas.Api.Impuesto.Avisos;

/// <summary>
/// Purga de <c>consultas.vehicle_tax_webhook_deliveries</c> (HU #14711): cada <see cref="Intervalo"/> borra las entregas con más de
/// <see cref="Retencion"/>. Pasada la retención el proveedor ya no reintenta ese aviso (sus reintentos duran unas 21 h), así que la
/// deduplicación no se pierde. Una vuelta que falla se registra y se reintenta en la siguiente. Logs: solo conteos.
/// </summary>
/// <example>
/// <code>
/// builder.Services.AddHostedService&lt;PurgaAvisosImpuesto&gt;();
/// </code>
/// </example>
internal sealed partial class PurgaAvisosImpuesto(IRegistroAvisosImpuesto registro, TimeProvider time, ILogger<PurgaAvisosImpuesto> logger)
    : BackgroundService
{
    public static readonly TimeSpan Intervalo = TimeSpan.FromHours(1);
    public static readonly TimeSpan Retencion = TimeSpan.FromDays(7);

    public Task<int> PurgarAsync(CancellationToken ct) => registro.PurgarAsync(time.GetUtcNow() - Retencion, ct);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(Intervalo, time, stoppingToken).ConfigureAwait(false);
                var borradas = await PurgarAsync(stoppingToken).ConfigureAwait(false);
                if (borradas > 0)
                    Purgadas(logger, borradas);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
#pragma warning disable CA1031 // Una vuelta que falla (base caída) no tumba el servicio: se reintenta en la siguiente.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                VueltaFallida(logger, ex.GetType().Name);
            }
        }
    }

    [LoggerMessage(EventId = 7461, Level = LogLevel.Information, Message = "Purga de avisos de impuesto: {Cantidad} entregas pasaron la retención y se borraron.")]
    private static partial void Purgadas(ILogger logger, int cantidad);

    [LoggerMessage(EventId = 7462, Level = LogLevel.Warning, Message = "La purga de avisos de impuesto falló ({Tipo}); se reintenta en la siguiente vuelta.")]
    private static partial void VueltaFallida(ILogger logger, string tipo);
}
