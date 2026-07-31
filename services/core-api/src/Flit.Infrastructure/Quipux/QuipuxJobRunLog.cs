using Flit.Infrastructure.Persistence;
using Flit.Modules.Quipux.Domain.Trazabilidad;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Flit.Infrastructure.Quipux;

/// <summary>
/// Persiste la bitácora de ejecuciones del worker en <c>admin.quipux_job_runs</c>. Complementa a
/// <see cref="QuipuxSubmissionAuditLog"/>: aquella traza el trámite, esta traza el JOB. Responde
/// "¿corrió el cron?", "¿cuánto tardó?", "¿cuántos reclamó?", "¿cuántos falló?" — preguntas que
/// con las Lambdas de FLIT 1.0 no tenían respuesta porque el scheduler no dejaba rastro.
///
/// PORQUÉ SCOPE PROPIO: mismo patrón que <see cref="QuipuxSubmissionAuditLog"/> y
/// <c>IdentityValidationAuditLog</c> — scope y <see cref="FlitDbContext"/> propios vía
/// <see cref="IServiceScopeFactory"/> y <c>SaveChanges</c> inmediato. El <c>StartAsync</c> debe
/// quedar committeado ANTES de que el ciclo empiece a trabajar: si esperara al final, un worker
/// que muere a media tanda no dejaría ninguna fila y el ciclo perdido sería invisible.
///
/// PORQUÉ TRAGA EXCEPCIONES: que no se pueda escribir la bitácora del job jamás debe tumbar el
/// job. Todo fallo se degrada a Warning.
///
/// SIN RLS: <c>admin.quipux_job_runs</c> no tiene <c>tenant_id</c> — el job es global, procesa
/// todos los tenants — así que la tabla no lleva política <c>tenant_isolation</c> y aquí no hace
/// falta fijar el GUC <c>app.current_tenant_id</c> ni abrir transacción explícita (a diferencia de
/// <see cref="QuipuxSubmissionAuditLog"/>, cuya tabla sí está aislada por tenant).
/// </summary>
/// <remarks>
/// SEÑAL DE DIAGNÓSTICO: una fila con <c>finished_at</c> NULL y <c>started_at</c> viejo delata un
/// worker que murió a medio ciclo (proceso reiniciado, pod evictado, excepción no capturada fuera
/// del try del ciclo). El DDL trae el índice parcial <c>ix_quipux_job_runs_en_curso</c>
/// (<c>WHERE finished_at IS NULL</c>) precisamente para que esa consulta sea barata. Vale la pena
/// vigilarla: ningún health check del repo detecta un <c>BackgroundService</c> muerto — el host
/// sigue reportando healthy mientras su hosted service está caído, así que esta tabla es la única
/// evidencia de que el cron dejó de correr.
/// </remarks>
internal sealed class QuipuxJobRunLog(
    IServiceScopeFactory scopeFactory,
    ILogger<QuipuxJobRunLog> logger) : IQuipuxJobRunLog
{
    /// <summary>Tope de <c>admin.quipux_job_runs.error_message</c> (varchar(1000)).</summary>
    private const int MaxErrorMessageLength = 1000;

    /// <summary>
    /// Vocabulario cerrado de <c>job_name</c>. Espeja el CHECK del DDL: validar aquí evita que un
    /// nombre inválido reviente el INSERT y que el catch lo entierre en silencio.
    /// </summary>
    private static readonly HashSet<string> JobNamesValidos = new(StringComparer.Ordinal)
    {
        QuipuxJobNames.Register,
        QuipuxJobNames.StatusPoll,
    };

    /// <inheritdoc />
    /// <remarks>
    /// Devuelve SIEMPRE un id, incluso si la escritura falla o el nombre es inválido: el job no
    /// debe detenerse por su bitácora. En ese caso <see cref="FinishAsync"/> no encontrará la fila
    /// y también se degradará a Warning — se pierde la traza del ciclo, no el ciclo.
    /// </remarks>
    public async Task<Guid> StartAsync(string jobName, CancellationToken cancellationToken = default)
    {
        var runId = Guid.NewGuid();

        if (!JobNamesValidos.Contains(jobName))
        {
            QuipuxJobRunLogMessages.JobNameInvalido(logger, jobName);
            return runId;
        }

        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<FlitDbContext>();

            db.QuipuxJobRuns.Add(new QuipuxJobRun
            {
                Id = runId,
                JobName = jobName,
                StartedAt = DateTimeOffset.UtcNow,
                FinishedAt = null, // se estampa en FinishAsync; NULL = ciclo en curso
                ClaimedCount = 0,
                OkCount = 0,
                ErrorCount = 0,
                ErrorMessage = null,
            });

            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            QuipuxJobRunLogMessages.InicioFallido(logger, jobName, ex);
        }

        return runId;
    }

    /// <inheritdoc />
    public async Task FinishAsync(
        Guid runId,
        int claimedCount,
        int okCount,
        int errorCount,
        string? errorMessage = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<FlitDbContext>();

            var run = await db.QuipuxJobRuns.FirstOrDefaultAsync(r => r.Id == runId, cancellationToken);
            if (run is null)
            {
                // El StartAsync no llegó a persistir (ver su remark) o el id no es de este job.
                QuipuxJobRunLogMessages.CierreSinFila(logger, runId);
                return;
            }

            run.FinishedAt = DateTimeOffset.UtcNow;
            run.ClaimedCount = claimedCount;
            run.OkCount = okCount;
            run.ErrorCount = errorCount;

            // error_message es varchar(1000): truncar ANTES o el INSERT/UPDATE falla (22001) y el
            // catch se lo tragaría, dejando la fila eternamente sin finished_at — es decir,
            // indistinguible de un worker muerto. Aquí solo va el error que aborta el ciclo
            // entero; el fallo de un elemento va al evento de su submission.
            run.ErrorMessage = Truncar(errorMessage, MaxErrorMessageLength);

            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            QuipuxJobRunLogMessages.CierreFallido(logger, runId, ex);
        }
    }

    private static string? Truncar(string? value, int max) =>
        value is not null && value.Length > max ? value[..max] : value;
}

/// <summary>Logging source-generated (CA1848) de la bitácora de ejecuciones del worker Quipux.</summary>
internal static partial class QuipuxJobRunLogMessages
{
    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Nombre de job Quipux fuera del vocabulario (job={JobName}); no se abre bitácora de ejecución.")]
    public static partial void JobNameInvalido(ILogger logger, string jobName);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "No se pudo abrir la bitácora de ejecución Quipux (job={JobName}); el ciclo continúa sin traza.")]
    public static partial void InicioFallido(ILogger logger, string jobName, Exception ex);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "No existe la fila de ejecución Quipux a cerrar (run={RunId}); su apertura no llegó a persistir.")]
    public static partial void CierreSinFila(ILogger logger, Guid runId);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "No se pudo cerrar la bitácora de ejecución Quipux (run={RunId}); quedará con finished_at NULL.")]
    public static partial void CierreFallido(ILogger logger, Guid runId, Exception ex);
}
