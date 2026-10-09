using System.Threading.Channels;
using Flit.Comparendos.Api.Modulos.Calendario;
using Flit.Comparendos.Api.Persistence;
using Flit.Comparendos.Api.Plataforma;
using Flit.Comparendos.Domain.Calendario;
using Flit.Comparendos.Domain.Modelo;
using Flit.Comparendos.Domain.Tiempo;
using Microsoft.EntityFrameworkCore;

namespace Flit.Comparendos.Api.Modulos.Trabajos;

/// <summary>Una ejecución pedida: programada (cron) o manual (con la empresa y el usuario que la pidió).</summary>
public sealed record SolicitudTrabajo(TipoTrabajo Tipo, Disparo Disparo, Guid? TenantId = null, Guid? UsuarioId = null);

/// <summary>Lo que hace cada tipo de trabajo. Cada módulo registra el suyo (ingesta, avisos, contraventor, reportes).</summary>
public interface IEjecutorTrabajo
{
    bool Atiende(TipoTrabajo tipo);

    Task EjecutarAsync(SolicitudTrabajo solicitud, CancellationToken ct);
}

/// <summary>Cola en memoria de ejecuciones pendientes (la consumen <see cref="ProcesadorTrabajos"/>).</summary>
public sealed class ColaTrabajos
{
    private readonly Channel<SolicitudTrabajo> _canal = Channel.CreateUnbounded<SolicitudTrabajo>();

    public ValueTask EncolarAsync(SolicitudTrabajo solicitud, CancellationToken ct) => _canal.Writer.WriteAsync(solicitud, ct);

    public IAsyncEnumerable<SolicitudTrabajo> LeerAsync(CancellationToken ct) => _canal.Reader.ReadAllAsync(ct);
}

/// <summary>
/// Revisa cada 20 segundos qué trabajos tocan en el minuto actual (hora de Colombia) y los encola. El reclamo es un
/// UPDATE condicionado sobre <c>ultima_ejecucion_en</c>: si hay varias réplicas, solo una gana cada minuto. Los trabajos
/// marcados «solo días hábiles» no corren en sábados, domingos ni festivos.
/// </summary>
internal sealed partial class ProgramadorTrabajos(IServiceScopeFactory scopes, ColaTrabajos cola, CalendarioService calendario, TimeProvider time, ILogger<ProgramadorTrabajos> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(10), time, stoppingToken).ConfigureAwait(false);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RevisarAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogFallo(logger, ex);
            }

            await Task.Delay(TimeSpan.FromSeconds(20), time, stoppingToken).ConfigureAwait(false);
        }
    }

    internal async Task RevisarAsync(CancellationToken ct)
    {
        var ahora = time.GetUtcNow();
        var local = ColombiaTime.From(ahora).DateTime;
        var minuto = new DateTimeOffset(local.Year, local.Month, local.Day, local.Hour, local.Minute, 0, ColombiaTime.Offset).ToUniversalTime();
        var habil = (await calendario.ActualAsync(ct).ConfigureAwait(false)).EsHabil(DateOnly.FromDateTime(local));

        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ComparendosDb>();
        var trabajos = await db.TrabajosProgramados.AsNoTracking().Where(t => t.Activo).ToListAsync(ct).ConfigureAwait(false);
        foreach (var t in trabajos)
        {
            if (t.SoloDiasHabiles && !habil)
                continue;
            if (!ExpresionCron.EsValida(t.ExpresionCron, out _) || !ExpresionCron.Parse(t.ExpresionCron).Coincide(local))
                continue;

            var ganado = await db.TrabajosProgramados
                .Where(x => x.Id == t.Id && (x.UltimaEjecucionEn == null || x.UltimaEjecucionEn < minuto))
                .ExecuteUpdateAsync(u => u.SetProperty(x => x.UltimaEjecucionEn, ahora), ct).ConfigureAwait(false);
            if (ganado == 1)
                await cola.EncolarAsync(new SolicitudTrabajo(t.Tipo, Disparo.Programado), ct).ConfigureAwait(false);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Falló la revisión de trabajos programados de Comparendos.")]
    private static partial void LogFallo(ILogger logger, Exception ex);
}

/// <summary>Ejecuta lo que llega a la cola, de a dos a la vez, cada uno en su propio scope.</summary>
internal sealed partial class ProcesadorTrabajos(IServiceScopeFactory scopes, ColaTrabajos cola, ILogger<ProcesadorTrabajos> logger) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        Parallel.ForEachAsync(cola.LeerAsync(stoppingToken), new ParallelOptions { MaxDegreeOfParallelism = 2, CancellationToken = stoppingToken }, async (solicitud, ct) =>
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var ejecutor = scope.ServiceProvider.GetServices<IEjecutorTrabajo>().FirstOrDefault(e => e.Atiende(solicitud.Tipo));
                if (ejecutor is null)
                {
                    LogSinEjecutor(logger, solicitud.Tipo);
                    return;
                }

                await ejecutor.EjecutarAsync(solicitud, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogFallo(logger, solicitud.Tipo, ex);
            }
        });

    [LoggerMessage(Level = LogLevel.Warning, Message = "No hay quien ejecute el trabajo {Tipo}.")]
    private static partial void LogSinEjecutor(ILogger logger, TipoTrabajo tipo);

    [LoggerMessage(Level = LogLevel.Error, Message = "Falló el trabajo {Tipo} de Comparendos.")]
    private static partial void LogFallo(ILogger logger, TipoTrabajo tipo, Exception ex);
}
