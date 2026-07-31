using Flit.Modules.Quipux.Application.UseCases.ConsultarEstado;
using Flit.Modules.Quipux.Domain.Configuracion;
using Flit.Modules.Quipux.Domain.Envios;
using Flit.Modules.Quipux.Domain.Trazabilidad;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Flit.Infrastructure.Quipux;

/// <summary>
/// Consultor del estado de los trámites ya radicados en Quipux. Sustituye al job
/// <c>validateStatusDocument</c> de FLIT 1.0 (Lambda AWS con cron cada 15 min): en 2.0 corre en
/// una VPS y <c>ADR-0024</c> RECHAZA explícitamente cron/broker externo (Hangfire, Quartz, colas)
/// para este mismo problema, así que el "cron" es este <see cref="BackgroundService"/> dentro de
/// core-api. Es POLLING y no webhook por decisión explícita del usuario para esta integración.
/// </summary>
/// <remarks>
/// <para>Cada ciclo reclama submissions en <c>registrado</c> y pregunta a Quipux si la secretaría
/// ya resolvió el trámite: <c>estadoTramite.codigo = 2</c> lo aprueba y <c>3</c> lo rechaza con
/// motivo. La transición del trámite la empuja el handler, no el worker.</para>
/// <para><b>Ventana de frescura.</b> El claim exige que la última consulta sea anterior a
/// <c>now - pollIntervalMinutes</c>, así que una submission sondeada hace un momento NO se vuelve
/// a sondear en el ciclo siguiente. Es el patrón de <c>IdentityValidationReconcileProcessor</c>:
/// sin ese corte, un puñado de submissions vivas se sondearía en bucle cerrado y le pegaría a
/// Quipux muchas más veces de las que la cadencia promete. El presupuesto
/// <see cref="QuipuxSettings.MaxPolls"/> acota además el sondeo de un trámite que Quipux nunca
/// resuelve.</para>
/// <para><b>Por qué NO hay un tercer worker.</b> FLIT 1.0 tenía además
/// <c>validateStatusDocumentTimeOut</c>, que rastreaba los registros cuyo POST expiró (podían
/// estar radicados en Quipux sin que FLIT lo supiera). Ese job se absorbe en
/// <c>QuipuxRegisterProcessor</c>: como el <c>document_name</c> es estable entre reintentos,
/// <c>RegistrarDocumentoQuipuxHandler</c> consulta antes de re-registrar cuando
/// <see cref="QuipuxSubmission.RequiereConsultaPrevia"/>. Este worker solo mira lo ya confirmado
/// como <c>registrado</c>.</para>
/// <para><b>Cadencia configurable en BD.</b> El intervalo NO es una constante: se relee de
/// <c>admin.quipux_settings</c> (<see cref="QuipuxSettings.PollIntervalMinutes"/>, default 15) en
/// CADA ciclo. Un <c>UPDATE admin.quipux_settings SET poll_interval_minutes = 1</c> surte efecto
/// en el siguiente ciclo, sin desplegar ni reiniciar el proceso.</para>
/// <para><b>El gate es la BD, no el entorno.</b> Sin fila o con <c>enabled = false</c> el worker
/// es un no-op silencioso. NO se usa <c>IsDevelopment()</c>: el compose de PDN corre con
/// <c>ASPNETCORE_ENVIRONMENT=Development</c>, así que ese gate encendería la integración en
/// producción y no la apagaría en ningún lado.</para>
/// <para><b>Cross-tenant.</b> El worker barre todos los tenants. Funciona porque el rol de la
/// aplicación es dueño de las tablas y ninguna declara <c>FORCE ROW LEVEL SECURITY</c>, así que
/// las policies <c>tenant_isolation</c> no lo filtran — el mismo supuesto sobre el que ya corren
/// los 5 workers en producción.</para>
/// </remarks>
internal sealed class QuipuxStatusPollProcessor(
    IServiceScopeFactory scopeFactory,
    ILogger<QuipuxStatusPollProcessor> logger) : BackgroundService
{
    /// <summary>
    /// Retraso antes del primer ciclo, como en los 5 workers ya en producción.
    /// </summary>
    /// <remarks>
    /// Con la configuración por defecto NO es lo que evita sondear tablas inexistentes: las
    /// migraciones corren síncronas (<c>Program.cs:129</c>, <c>Database:AutoMigrate</c> default
    /// true) entre <c>builder.Build()</c> y <c>app.Run()</c>, y los hosted services no arrancan
    /// hasta <c>app.Run()</c>. La pausa cubre lo demás: (1) si se delega el migrado al CD
    /// (<c>Database__AutoMigrate=false</c>), un paso de migración externo puede correr en paralelo
    /// al arranque y ahí sí habría carrera; (2) mantiene al worker fuera del pool de conexiones
    /// mientras el seed y los primeros requests compiten por él. Se desfasa de
    /// <c>QuipuxRegisterProcessor</c> (15 s) para que los dos no arranquen a la vez.
    /// </remarks>
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(20);

    /// <summary>
    /// Espera cuando la integración está apagada o sin fila. Corta y barata: solo cuesta un SELECT
    /// a una tabla de una fila, y hace que ENCENDER la integración se note en ~1 min en vez de en
    /// un intervalo completo (15 min).
    /// </summary>
    private static readonly TimeSpan IdleInterval = TimeSpan.FromSeconds(60);

    /// <summary>Cadencia por defecto si la fila trae un intervalo absurdo (&lt;= 0).</summary>
    private const int DefaultIntervalMinutes = 15;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(StartupDelay, stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            var interval = IdleInterval;
            try
            {
                interval = await RunCycleAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Try/catch POR CICLO: ni un fallo de BD ni un bug tumban el loop; se reintenta.
                QuipuxStatusPollLog.CycleError(logger, ex);
            }

            try { await Task.Delay(interval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    /// <summary>Ejecuta un ciclo completo y devuelve cuánto esperar hasta el siguiente (leído de BD).</summary>
    private async Task<TimeSpan> RunCycleAsync(CancellationToken ct)
    {
        var settings = await LoadSettingsAsync(ct);

        // Apagado o sin fila ⇒ no-op SILENCIOSO: ni log ni fila en quipux_job_runs. Registrar una
        // ejecución cada minuto con la integración apagada llenaría admin.quipux_job_runs de ruido
        // y la inutilizaría para lo que existe: responder "¿corrió el cron?" de un vistazo.
        if (settings is null || !settings.Enabled)
            return IdleInterval;

        var interval = ResolveInterval(settings.PollIntervalMinutes);

        // Encendido pero a medio configurar SÍ se registra: es un error del operador (activó el
        // toggle y la integración no consulta nada) y tiene que ser visible en la bitácora del job.
        if (!settings.EstaCompleta())
        {
            var incompleteRunId = await StartRunAsync(ct);
            QuipuxStatusPollLog.ConfiguracionIncompleta(logger);
            await FinishRunAsync(incompleteRunId, 0, 0, 0,
                "Configuración Quipux incompleta (quipux_settings): el ciclo no se ejecutó.", ct);
            return interval;
        }

        var runId = await StartRunAsync(ct);

        // Correlaciona todos los eventos de bitácora de ESTE ciclo (y con admin.ot_api_call_logs).
        var correlationId = Guid.NewGuid();
        var claimed = 0;
        var ok = 0;
        var errors = 0;
        string? cycleError = null;

        try
        {
            var counters = await SondearRegistradasAsync(settings, interval, correlationId, ct);
            claimed = counters.Total;
            ok = counters.Ok;
            errors = counters.Errors;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Fallo que aborta el ciclo entero (no el de un elemento: ese ya se contó en errors).
            cycleError = Truncate(ex.GetBaseException().Message);
            QuipuxStatusPollLog.CycleError(logger, ex);
        }

        await FinishRunAsync(runId, claimed, ok, errors, cycleError, ct);

        if (claimed > 0)
            QuipuxStatusPollLog.CicloCompletado(logger, claimed, ok, errors);

        return interval;
    }

    /// <summary>
    /// Reclama submissions <c>registrado</c> fuera de la ventana de frescura y consulta su estado
    /// en Quipux, hasta agotar el lote o hasta que no quede nada reclamable.
    /// </summary>
    /// <remarks>
    /// El corte de frescura es <c>now - pollIntervalMinutes</c>: se deriva del MISMO intervalo del
    /// ciclo, así que bajar la cadencia a 1 min estrecha la ventana con ella y el toggle sin deploy
    /// sigue siendo coherente. El claim ya estampa <c>last_polled_at = now</c> (y consume
    /// presupuesto) al reclamar, así que la fila queda fuera de la ventana y no se re-sondea en
    /// este ciclo; el set <c>vistas</c> es redundante con eso y se queda como red por si el corte
    /// quedara degenerado (intervalo mínimo), garantizando <b>un sondeo por submission y por
    /// ciclo</b> y que no se le queme el presupuesto de <see cref="QuipuxSettings.MaxPolls"/>.
    /// </remarks>
    private async Task<CycleCounters> SondearRegistradasAsync(
        QuipuxSettings settings, TimeSpan interval, Guid correlationId, CancellationToken ct)
    {
        var stalenessCutoff = DateTimeOffset.UtcNow - interval;
        var vistas = new HashSet<Guid>();
        var ok = 0;
        var errors = 0;

        for (var i = 0; i < settings.BatchSize; i++)
        {
            if (ct.IsCancellationRequested)
                break;

            QuipuxSubmission? submission;
            await using (var claimScope = scopeFactory.CreateAsyncScope())
            {
                var repo = claimScope.ServiceProvider.GetRequiredService<IQuipuxSubmissionRepository>();
                submission = await repo.ClaimNextRegistradaAsync(stalenessCutoff, settings.MaxPolls, ct);
            }

            if (submission is null)
                break; // no queda nada reclamable en este ciclo

            if (!vistas.Add(submission.Id))
                break; // el repositorio ya solo devuelve filas de este ciclo: se corta aquí

            try
            {
                // Scope PROPIO por unidad de trabajo, separado del claim: el handler abre su
                // propia transacción y no debe compartir la conexión que sostuvo el claim.
                // Nunca se inyecta un DbContext en este singleton.
                await using var scope = scopeFactory.CreateAsyncScope();
                var handler = scope.ServiceProvider.GetRequiredService<ConsultarEstadoQuipuxHandler>();
                var result = await handler.HandleAsync(
                    new ConsultarEstadoQuipuxCommand
                    {
                        SubmissionId = submission.Id,
                        CorrelationId = correlationId,
                    },
                    ct);

                switch (result.Status)
                {
                    // EnTramite es el desenlace MAYORITARIO y es un sondeo EXITOSO: Quipux
                    // respondió, la secretaría simplemente aún no resuelve. Contarlo como error
                    // haría que error_count creciera sin parar en la operación normal.
                    case ConsultarEstadoQuipuxStatus.EnTramite:
                    case ConsultarEstadoQuipuxStatus.Aprobado:
                    case ConsultarEstadoQuipuxStatus.Rechazado:
                        ok++;
                        if (result.EsTerminal)
                            QuipuxStatusPollLog.Resuelta(
                                logger, submission.Id, submission.ProcedureInstanceId,
                                result.Status, result.EstadoTramiteCodigo ?? 0);
                        break;

                    case ConsultarEstadoQuipuxStatus.Deshabilitado:
                    case ConsultarEstadoQuipuxStatus.NoEncontrado:
                    case ConsultarEstadoQuipuxStatus.EstadoInvalido:
                        // Carrera con otra réplica o con el toggle: nada que hacer ni que contar.
                        QuipuxStatusPollLog.ConsultaOmitida(logger, submission.Id, result.Status);
                        break;

                    default:
                        // ErrorConsulta / TransicionFallida. El handler ya escribió el evento en la
                        // bitácora de la submission; aquí solo se cuenta para el run log.
                        errors++;
                        QuipuxStatusPollLog.ConsultaNoExitosa(
                            logger, submission.Id, submission.ProcedureInstanceId,
                            result.Status, result.Motivo ?? "-");
                        break;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Try/catch POR ELEMENTO: una submission envenenada no tumba el lote ni el loop.
                // No se reintenta dentro del ciclo: la ventana de frescura ya la reprograma.
                errors++;
                QuipuxStatusPollLog.ConsultaError(logger, submission.Id, submission.ProcedureInstanceId, ex);
            }

            // Dead-letter OBSERVABLE del sondeo. Agotar MaxPolls significa que Quipux lleva
            // MaxPolls x intervalo sin resolver el trámite: la submission deja de reclamarse (el
            // claim filtra por poll_count < MaxPolls) y se queda en `registrado` para siempre, así
            // que sin este rastro nadie se enteraría. Va fuera del try/catch porque no depende del
            // desenlace: es el presupuesto, ya consumido por el claim.
            if (submission.PollCount >= settings.MaxPolls)
                QuipuxStatusPollLog.PresupuestoAgotado(
                    logger, submission.Id, submission.ProcedureInstanceId, submission.PollCount);
        }

        return new CycleCounters(vistas.Count, ok, errors);
    }

    private async Task<QuipuxSettings?> LoadSettingsAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var repo = scope.ServiceProvider.GetRequiredService<IQuipuxSettingsRepository>();
        return await repo.GetAsync(ct);
    }

    private async Task<Guid> StartRunAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var runLog = scope.ServiceProvider.GetRequiredService<IQuipuxJobRunLog>();
        return await runLog.StartAsync(QuipuxJobNames.StatusPoll, ct);
    }

    private async Task FinishRunAsync(
        Guid runId, int claimed, int ok, int errors, string? errorMessage, CancellationToken ct)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var runLog = scope.ServiceProvider.GetRequiredService<IQuipuxJobRunLog>();
            await runLog.FinishAsync(runId, claimed, ok, errors, errorMessage, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // La bitácora del job NUNCA rompe el worker: una fila sin finished_at ya delata por sí
            // sola el ciclo que no cerró.
            QuipuxStatusPollLog.JobRunLogError(logger, runId, ex);
        }
    }

    private static TimeSpan ResolveInterval(int minutes) =>
        TimeSpan.FromMinutes(minutes > 0 ? minutes : DefaultIntervalMinutes);

    /// <summary>Recorta el mensaje para <c>quipux_job_runs.error_message</c> (evita textos enormes de EF/Npgsql).</summary>
    private static string Truncate(string message) =>
        string.IsNullOrEmpty(message) || message.Length <= 300 ? message : message[..300];

    /// <summary>
    /// Contadores de una pasada. <c>Total</c> son las unidades tomadas; el resto que no es
    /// <c>Ok</c> ni <c>Errors</c> son omisiones esperadas (carreras), que no se cuentan como fallo
    /// para no disparar alertas por el caso normal.
    /// </summary>
    private readonly record struct CycleCounters(int Total, int Ok, int Errors);
}

/// <summary>Logging source-generated (CA1848, obligatorio por analizador) del consultor de estado Quipux. Sin PII: solo ids y códigos.</summary>
internal static partial class QuipuxStatusPollLog
{
    [LoggerMessage(Level = LogLevel.Information,
        Message = "Quipux consulta: ciclo completado (unidades={Claimed}, ok={Ok}, errores={Errors}).")]
    public static partial void CicloCompletado(ILogger logger, int claimed, int ok, int errors);

    // Los enums viajan SIN ToString() en el call site: el generador solo los formatea si el nivel
    // está habilitado. Hacerlo a mano dispara CA1873 (evaluación cara e innecesaria si el log está
    // apagado), que en este repo es error, no advertencia.

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Quipux consulta: la submission {SubmissionId} (trámite {ProcedureInstanceId}) fue resuelta por la secretaría → {Status} (código {Codigo}).")]
    public static partial void Resuelta(
        ILogger logger, Guid submissionId, Guid procedureInstanceId,
        ConsultarEstadoQuipuxStatus status, int codigo);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Quipux consulta: la integración está habilitada pero quipux_settings está incompleta; el ciclo no se ejecutó.")]
    public static partial void ConfiguracionIncompleta(ILogger logger);

    [LoggerMessage(Level = LogLevel.Debug,
        Message = "Quipux consulta: la submission {SubmissionId} no se sondeó ({Status}); otra réplica o el toggle la resolvieron.")]
    public static partial void ConsultaOmitida(
        ILogger logger, Guid submissionId, ConsultarEstadoQuipuxStatus status);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Quipux consulta: el sondeo de la submission {SubmissionId} (trámite {ProcedureInstanceId}) no fue exitoso ({Status}: {Motivo}); se reintentará.")]
    public static partial void ConsultaNoExitosa(
        ILogger logger, Guid submissionId, Guid procedureInstanceId,
        ConsultarEstadoQuipuxStatus status, string motivo);

    [LoggerMessage(Level = LogLevel.Error,
        Message = "Quipux consulta: falló la consulta de estado de la submission {SubmissionId} (trámite {ProcedureInstanceId}); se reintentará en el próximo ciclo.")]
    public static partial void ConsultaError(ILogger logger, Guid submissionId, Guid procedureInstanceId, Exception ex);

    [LoggerMessage(Level = LogLevel.Critical,
        Message = "Quipux consulta: la submission {SubmissionId} (trámite {ProcedureInstanceId}) agotó el presupuesto de sondeo ({PollCount}); Quipux nunca resolvió el trámite y NO se volverá a consultar. Requiere revisión manual.")]
    public static partial void PresupuestoAgotado(
        ILogger logger, Guid submissionId, Guid procedureInstanceId, int pollCount);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Quipux consulta: no se pudo cerrar la bitácora de la ejecución {RunId}.")]
    public static partial void JobRunLogError(ILogger logger, Guid runId, Exception ex);

    [LoggerMessage(Level = LogLevel.Error,
        Message = "Quipux consulta: error en el ciclo de sondeo; se reintentará.")]
    public static partial void CycleError(ILogger logger, Exception ex);
}
