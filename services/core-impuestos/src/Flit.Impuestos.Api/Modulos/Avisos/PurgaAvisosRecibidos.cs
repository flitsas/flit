using Flit.Impuestos.Api.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Flit.Impuestos.Api.Modulos.Avisos;

/// <summary>
/// Purga de <c>impuestos.avisos_recibidos</c> (security B-3; plan §4.1 y <see cref="AvisoRecibido"/>): cada
/// <see cref="ClaveIntervalo"/> (1 hora por defecto) borra las filas con <c>recibido_en</c> anterior a la retención
/// (<see cref="ClaveRetencion"/>, 30 días por defecto) en lotes de <see cref="ClaveLote"/> (1000) con <c>ExecuteDelete</c>, hasta
/// que no quede ninguna vieja. La tabla es global (sin empresa) y no guarda cuerpo ni PII. Con <see cref="ClaveActiva"/>=false no
/// corre en segundo plano. Una vuelta que falla se registra y se reintenta en la siguiente. Logs: solo conteos.
/// </summary>
/// <example>
/// <code>
/// Impuestos__Avisos__Purga__Intervalo=01:00:00
/// Impuestos__Avisos__Purga__Retencion=30.00:00:00
/// var borradas = await purga.PurgarAsync(ct);
/// </code>
/// </example>
public sealed partial class PurgaAvisosRecibidos(
    IServiceScopeFactory scopes, IConfiguration configuracion, TimeProvider reloj, ILogger<PurgaAvisosRecibidos> log) : BackgroundService
{
    public const string ClaveActiva = "Impuestos:Avisos:Purga:Activa";
    public const string ClaveIntervalo = "Impuestos:Avisos:Purga:Intervalo";
    public const string ClaveRetencion = "Impuestos:Avisos:Purga:Retencion";
    public const string ClaveLote = "Impuestos:Avisos:Purga:Lote";

    public static readonly TimeSpan IntervaloPorDefecto = TimeSpan.FromHours(1);
    public static readonly TimeSpan RetencionPorDefecto = TimeSpan.FromDays(30);
    public const int LotePorDefecto = 1000;

    /// <summary>Borra por lotes lo que pasó la retención; devuelve cuántas filas borró.</summary>
    public async Task<int> PurgarAsync(CancellationToken ct)
    {
        var retencion = configuracion.GetValue(ClaveRetencion, RetencionPorDefecto);
        if (retencion <= TimeSpan.Zero)
            retencion = RetencionPorDefecto;
        var lote = configuracion.GetValue(ClaveLote, LotePorDefecto);
        if (lote <= 0)
            lote = LotePorDefecto;
        var limite = reloj.GetUtcNow() - retencion;

        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ImpuestosDb>();
        var total = 0;
        int borradas;
        do
        {
            borradas = await db.Set<AvisoRecibido>()
                .Where(a => a.RecibidoEn < limite)
                .OrderBy(a => a.RecibidoEn)
                .Take(lote)
                .ExecuteDeleteAsync(ct).ConfigureAwait(false);
            total += borradas;
        }
        while (borradas == lote && !ct.IsCancellationRequested);

        if (total > 0)
            Purgadas(log, total);
        return total;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var intervalo = configuracion.GetValue(ClaveIntervalo, IntervaloPorDefecto);
        if (intervalo <= TimeSpan.Zero)
            intervalo = IntervaloPorDefecto;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(intervalo, reloj, stoppingToken).ConfigureAwait(false);
                await PurgarAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
#pragma warning disable CA1031 // Una vuelta que falla (base caída) no tumba el servicio: se reintenta en la siguiente.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                VueltaFallida(log, ex.GetType().Name);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Purga de avisos recibidos: {Cantidad} filas pasaron la retención y se borraron.")]
    private static partial void Purgadas(ILogger logger, int cantidad);

    [LoggerMessage(Level = LogLevel.Warning, Message = "La purga de avisos recibidos falló ({Tipo}); se reintenta en la siguiente vuelta.")]
    private static partial void VueltaFallida(ILogger logger, string tipo);
}
