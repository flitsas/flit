using Flit.Infrastructure.Persistence;
using Flit.Modules.Quipux.Application.UseCases.EncolarEnvio;
using Flit.Modules.Quipux.Application.UseCases.RegistrarDocumento;
using Flit.Modules.Quipux.Domain.Configuracion;
using Flit.Modules.Quipux.Domain.Envios;
using Flit.Modules.Quipux.Domain.Trazabilidad;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Flit.Infrastructure.Quipux;

/// <summary>
/// Radicador de trámites en Quipux. Sustituye al job <c>registerDocument</c> de FLIT 1.0 (una
/// Lambda AWS con cron cada 15 min): en 2.0 la integración corre en una VPS y <c>ADR-0024</c>
/// RECHAZA explícitamente cron/broker externo (Hangfire, Quartz, colas) para este mismo problema,
/// así que el "cron" es este <see cref="BackgroundService"/> dentro de core-api, con claim
/// <c>FOR UPDATE SKIP LOCKED</c> (en el repositorio) para que la ventana de redeploy —o un futuro
/// escalado a N réplicas— no radique el mismo trámite dos veces.
/// </summary>
/// <remarks>
/// <para>Cada ciclo hace dos pasadas:</para>
/// <list type="number">
/// <item><b>Encolar</b>: los trámites elegibles sin submission (<c>preparado</c> + OT en modo
/// <c>quipux</c> + <c>external_refs-&gt;'quipux'</c> + <c>codigoDivipo</c>) se convierten en
/// submissions <c>pendiente</c>. El encolado es idempotente
/// (<c>EnqueueIfAbsentAsync</c>), así que un ciclo solapado no duplica.</item>
/// <item><b>Radicar</b>: reclama submissions <c>pendiente</c> y las manda a Quipux.</item>
/// </list>
/// <para><b>Por qué son DOS workers y no TRES.</b> FLIT 1.0 tenía <c>registerDocument</c>,
/// <c>validateStatusDocument</c> y <c>validateStatusDocumentTimeOut</c>. El tercero existía porque
/// el <c>documento</c> se regeneraba en cada intento (precisión de minuto), de modo que un POST
/// que expiraba dejaba un trámite posiblemente radicado en Quipux e imposible de correlacionar:
/// hacía falta un job aparte que rastreara los timeouts. En 2.0 el <c>document_name</c> se calcula
/// UNA vez al encolar y es estable entre reintentos (ver
/// <see cref="QuipuxSubmission.DocumentName"/>), así que ese job se ABSORBE aquí:
/// <c>RegistrarDocumentoQuipuxHandler</c> consulta antes de re-registrar cuando
/// <see cref="QuipuxSubmission.RequiereConsultaPrevia"/> (hubo un intento previo fallido), porque
/// Quipux pudo haber radicado el documento pese al timeout HTTP.</para>
/// <para><b>Cadencia configurable en BD.</b> El intervalo NO es una constante: se relee de
/// <c>admin.quipux_settings</c> (<see cref="QuipuxSettings.RegisterIntervalMinutes"/>, default 15)
/// en CADA ciclo. Un <c>UPDATE admin.quipux_settings SET register_interval_minutes = 1</c> surte
/// efecto en el siguiente ciclo, sin desplegar ni reiniciar el proceso.</para>
/// <para><b>El gate es la BD, no el entorno.</b> Sin fila o con <c>enabled = false</c> el worker
/// es un no-op silencioso y vuelve a mirar en <see cref="IdleInterval"/>. NO se usa
/// <c>IsDevelopment()</c> para esto: el compose de PDN corre con
/// <c>ASPNETCORE_ENVIRONMENT=Development</c>, así que ese gate encendería la integración en
/// producción y no la apagaría en ningún lado.</para>
/// <para><b>Cross-tenant.</b> El worker barre todos los tenants. Funciona porque el rol de la
/// aplicación es dueño de las tablas y ninguna declara <c>FORCE ROW LEVEL SECURITY</c>, así que
/// las policies <c>tenant_isolation</c> (<c>tenant_id = current_setting('app.current_tenant_id')</c>)
/// no lo filtran — el mismo supuesto sobre el que ya corren los 5 workers en producción.</para>
/// </remarks>
internal sealed class QuipuxRegisterProcessor(
    IServiceScopeFactory scopeFactory,
    ILogger<QuipuxRegisterProcessor> logger) : BackgroundService
{
    /// <summary>
    /// Retraso antes del primer ciclo, como en los 5 workers ya en producción.
    /// </summary>
    /// <remarks>
    /// Con la configuración por defecto NO es lo que evita sondear tablas inexistentes: las
    /// migraciones corren síncronas (<c>Program.cs:129</c>, <c>Database:AutoMigrate</c> default
    /// true) entre <c>builder.Build()</c> y <c>app.Run()</c>, y los hosted services no arrancan
    /// hasta <c>app.Run()</c> — así que <c>admin.quipux_settings</c> y
    /// <c>tramites.quipux_submissions</c> ya existen en el primer tick. La pausa cubre lo demás:
    /// (1) si se delega el migrado al CD (<c>Database__AutoMigrate=false</c>), un paso de migración
    /// externo puede correr en paralelo al arranque y ahí sí habría carrera; (2) mantiene al worker
    /// fuera del pool de conexiones mientras el seed y los primeros requests compiten por él. Se
    /// desfasa de <c>QuipuxStatusPollProcessor</c> (20 s) para que los dos no arranquen a la vez.
    /// </remarks>
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(15);

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
                QuipuxRegisterLog.CycleError(logger, ex);
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

        var interval = ResolveInterval(settings.RegisterIntervalMinutes);

        // Encendido pero a medio configurar SÍ se registra: es un error del operador (activó el
        // toggle y la integración no radica nada) y tiene que ser visible en la bitácora del job.
        if (!settings.EstaCompleta())
        {
            var incompleteRunId = await StartRunAsync(ct);
            QuipuxRegisterLog.ConfiguracionIncompleta(logger);
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
            var encolados = await EncolarElegiblesAsync(settings.BatchSize, correlationId, ct);
            var radicados = await RadicarPendientesAsync(settings, correlationId, ct);

            claimed = encolados.Total + radicados.Total;
            ok = encolados.Ok + radicados.Ok;
            errors = encolados.Errors + radicados.Errors;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Fallo que aborta el ciclo entero (no el de un elemento: ese ya se contó en errors).
            cycleError = Truncate(ex.GetBaseException().Message);
            QuipuxRegisterLog.CycleError(logger, ex);
        }

        await FinishRunAsync(runId, claimed, ok, errors, cycleError, ct);

        if (claimed > 0)
            QuipuxRegisterLog.CicloCompletado(logger, claimed, ok, errors);

        return interval;
    }

    /// <summary>
    /// Pasada 1 — <b>encolar</b>: convierte en submission cada trámite elegible que aún no tenga
    /// una. Cada elemento va en su PROPIO scope y con su propio try/catch: un trámite con datos
    /// corruptos (p. ej. <c>external_refs</c> mal formado) no puede impedir que se encolen los
    /// demás.
    /// </summary>
    private async Task<CycleCounters> EncolarElegiblesAsync(
        int batchSize, Guid correlationId, CancellationToken ct)
    {
        IReadOnlyList<QuipuxTramiteElegible> elegibles;
        await using (var scope = scopeFactory.CreateAsyncScope())
        {
            var repo = scope.ServiceProvider.GetRequiredService<IQuipuxSubmissionRepository>();
            elegibles = await repo.ListElegiblesSinSubmissionAsync(batchSize, ct);
        }

        var ok = 0;
        var errors = 0;
        foreach (var elegible in elegibles)
        {
            if (ct.IsCancellationRequested)
                break;

            var procedureInstanceId = elegible.ProcedureInstanceId;

            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();

                var handler = scope.ServiceProvider.GetRequiredService<EncolarEnvioQuipuxHandler>();
                var result = await handler.HandleAsync(
                    new EncolarEnvioQuipuxCommand
                    {
                        ProcedureInstanceId = procedureInstanceId,
                        // El barrido ya trae el tenant: es cross-tenant y el tenant_id venía en la
                        // misma fila, así que resolverlo aparte era una lectura por trámite de más.
                        TenantId = elegible.TenantId,
                        CorrelationId = correlationId,
                    },
                    ct);

                if (result.TieneSubmission)
                {
                    ok++;
                }
                else if (result.Status is EncolarEnvioQuipuxStatus.NoElegible
                         or EncolarEnvioQuipuxStatus.NoEncontrado)
                {
                    // El barrido ya filtró por elegibilidad, así que llegar aquí significa una
                    // carrera (el trámite cambió de estado) o un dato incompleto (sin placa/VIN).
                    // No es un fallo del worker: se omite y el próximo ciclo lo reevalúa.
                    QuipuxRegisterLog.EncolarOmitido(
                        logger, procedureInstanceId, result.Status, result.Motivo ?? "-");
                }
                else
                {
                    // ErrorConsolidado / EntradaInvalida: sí es un fallo, y es reintentable.
                    errors++;
                    QuipuxRegisterLog.EncolarNoExitoso(
                        logger, procedureInstanceId, result.Status, result.Motivo ?? "-");
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Try/catch POR ELEMENTO: un trámite envenenado no tumba el lote ni el loop.
                errors++;
                QuipuxRegisterLog.EncolarError(logger, procedureInstanceId, ex);
            }
        }

        return new CycleCounters(elegibles.Count, ok, errors);
    }

    /// <summary>
    /// Momento antes del cual un lease se considera abandonado (worker muerto a media radicación).
    /// </summary>
    /// <remarks>
    /// Se deriva del timeout de Quipux, no es una constante: una radicación puede encadenar hasta
    /// tres llamadas lentas (consulta previa del reintento, subida a S3 y POST de registro), así
    /// que el lease debe cubrirlas con holgura. Un lease más corto que el trabajo real reabriría
    /// la fila mientras el POST sigue en vuelo y volvería a radicar el documento por duplicado —
    /// justo lo que el lease existe para impedir. El piso de 5 minutos protege el caso de un
    /// <c>TimeoutSeconds</c> configurado muy bajo en BD.
    /// </remarks>
    private static DateTimeOffset CalcularLeaseCutoff(QuipuxSettings settings)
    {
        var lease = TimeSpan.FromSeconds(Math.Max(settings.TimeoutSeconds * 4, 300));
        return DateTimeOffset.UtcNow - lease;
    }

    /// <summary>
    /// Pasada 2 — <b>radicar</b>: reclama submissions <c>pendiente</c> (con
    /// <c>attempts &lt; MaxAttempts</c> y lease libre o vencido) y las manda a Quipux, hasta agotar
    /// el lote o hasta que no quede nada reclamable.
    /// </summary>
    /// <remarks>
    /// <para>Quien impide que una submission se reintente en bucle dentro de un mismo ciclo es el
    /// <b>lease</b> del repositorio: el claim sella <c>claimed_at = now</c> junto con
    /// <c>attempts += 1</c>, y la fila no vuelve a ser reclamable hasta que ese sello quede antes
    /// de <see cref="CalcularLeaseCutoff"/> (~5 min). Sin él, como el claim commitea ANTES del POST
    /// a Quipux —que es lento—, el bucle podría reclamar la misma fila una y otra vez y quemar los
    /// <see cref="QuipuxSettings.MaxAttempts"/> intentos en segundos, mandando a dead-letter en el
    /// primer ciclo algo que un reintento 15 min después habría resuelto (p. ej. una caída
    /// momentánea de Quipux).</para>
    /// <para>El set <c>vistas</c> es redundante con ese lease y se queda como red: si alguien
    /// configurara <c>TimeoutSeconds</c> de forma que el lease quedara más corto que el ciclo, aún
    /// se garantiza <b>un intento por submission y por ciclo</b>.</para>
    /// </remarks>
    private async Task<CycleCounters> RadicarPendientesAsync(
        QuipuxSettings settings, Guid correlationId, CancellationToken ct)
    {
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
                submission = await repo.ClaimNextPendienteAsync(
                    settings.MaxAttempts, CalcularLeaseCutoff(settings), ct);
            }

            if (submission is null)
                break; // no queda nada reclamable en este ciclo

            if (!vistas.Add(submission.Id))
                break; // el repositorio ya solo devuelve reintentos de este ciclo: se corta aquí

            try
            {
                // Scope PROPIO por unidad de trabajo, separado del claim: el handler abre su
                // propia transacción y no debe compartir la conexión que sostuvo el claim.
                // Nunca se inyecta un DbContext en este singleton.
                await using var scope = scopeFactory.CreateAsyncScope();
                var handler = scope.ServiceProvider.GetRequiredService<RegistrarDocumentoQuipuxHandler>();
                var result = await handler.HandleAsync(
                    new RegistrarDocumentoQuipuxCommand
                    {
                        SubmissionId = submission.Id,
                        CorrelationId = correlationId,
                    },
                    ct);

                if (result.EstaRadicado)
                {
                    ok++;
                }
                else if (result.Status is RegistrarDocumentoQuipuxStatus.Deshabilitado
                         or RegistrarDocumentoQuipuxStatus.NoEncontrado
                         or RegistrarDocumentoQuipuxStatus.EstadoInvalido)
                {
                    // Carrera con otra réplica o con el toggle: no hay nada que hacer ni que contar.
                    QuipuxRegisterLog.RadicarOmitida(logger, submission.Id, result.Status);
                }
                else
                {
                    // ErrorTransitorio / ErrorDefinitivo / Fallido. El handler ya escribió el
                    // evento en la bitácora de la submission (y el Critical del dead-letter cuando
                    // Status == Fallido), así que aquí NO se duplica el Critical: solo se cuenta.
                    errors++;
                    QuipuxRegisterLog.RadicarNoExitosa(
                        logger, submission.Id, submission.ProcedureInstanceId,
                        result.Status, result.Motivo ?? "-");
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Try/catch POR ELEMENTO: una submission envenenada no tumba el lote ni el loop.
                errors++;
                QuipuxRegisterLog.RadicarError(logger, submission.Id, submission.ProcedureInstanceId, ex);

                // Dead-letter OBSERVABLE. Solo en la ruta de EXCEPCIÓN: si el handler devolvió
                // Fallido ya lo logueó él. Aquí el handler reventó antes de poder hacerlo, y
                // `attempts` (ya incrementado por el claim) dice que era el último intento: sin
                // este rastro, la submission se quedaría en `pendiente` sin ser reclamable nunca
                // más y en silencio.
                if (submission.Attempts >= settings.MaxAttempts)
                    QuipuxRegisterLog.DeadLettered(
                        logger, submission.Id, submission.ProcedureInstanceId, submission.Attempts);
            }
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
        return await runLog.StartAsync(QuipuxJobNames.Register, ct);
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
            QuipuxRegisterLog.JobRunLogError(logger, runId, ex);
        }
    }

    private static TimeSpan ResolveInterval(int minutes) =>
        TimeSpan.FromMinutes(minutes > 0 ? minutes : DefaultIntervalMinutes);

    /// <summary>Recorta el mensaje para <c>quipux_job_runs.error_message</c> (evita textos enormes de EF/Npgsql).</summary>
    private static string Truncate(string message) =>
        string.IsNullOrEmpty(message) || message.Length <= 300 ? message : message[..300];

    /// <summary>
    /// Contadores de una pasada. <c>Total</c> son las unidades tomadas; el resto que no es
    /// <c>Ok</c> ni <c>Errors</c> son omisiones esperadas (carreras, no-elegibles), que no se
    /// cuentan como fallo para no disparar alertas por el caso normal.
    /// </summary>
    private readonly record struct CycleCounters(int Total, int Ok, int Errors);
}

/// <summary>Logging source-generated (CA1848, obligatorio por analizador) del radicador Quipux. Sin PII: solo ids y códigos.</summary>
internal static partial class QuipuxRegisterLog
{
    [LoggerMessage(Level = LogLevel.Information,
        Message = "Quipux registro: ciclo completado (unidades={Claimed}, ok={Ok}, errores={Errors}).")]
    public static partial void CicloCompletado(ILogger logger, int claimed, int ok, int errors);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Quipux registro: la integración está habilitada pero quipux_settings está incompleta; el ciclo no se ejecutó.")]
    public static partial void ConfiguracionIncompleta(ILogger logger);

    // Los enums viajan SIN ToString() en el call site: el generador solo los formatea si el nivel
    // está habilitado. Hacerlo a mano dispara CA1873 (evaluación cara e innecesaria si el log
    // está apagado), que en este repo es error, no advertencia.

    [LoggerMessage(Level = LogLevel.Debug,
        Message = "Quipux registro: el trámite {ProcedureInstanceId} no se encoló ({Status}: {Motivo}); se reevaluará en el próximo ciclo.")]
    public static partial void EncolarOmitido(
        ILogger logger, Guid procedureInstanceId, EncolarEnvioQuipuxStatus status, string motivo);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Quipux registro: el encolado del trámite {ProcedureInstanceId} no fue exitoso ({Status}: {Motivo}); se reintentará.")]
    public static partial void EncolarNoExitoso(
        ILogger logger, Guid procedureInstanceId, EncolarEnvioQuipuxStatus status, string motivo);

    [LoggerMessage(Level = LogLevel.Error,
        Message = "Quipux registro: falló el encolado del trámite {ProcedureInstanceId}; se reintentará en el próximo ciclo.")]
    public static partial void EncolarError(ILogger logger, Guid procedureInstanceId, Exception ex);

    // El enum viaja SIN ToString(): el generador lo formatea solo si el nivel está habilitado. Un
    // .ToString() en el sitio de llamada se evalúa siempre, incluso con el log apagado (CA1873), y
    // estos dos son mensajes Debug — es decir, apagados en producción.
    [LoggerMessage(Level = LogLevel.Debug,
        Message = "Quipux registro: la submission {SubmissionId} no se radicó ({Status}); otra réplica o el toggle la resolvieron.")]
    public static partial void RadicarOmitida(ILogger logger, Guid submissionId, RegistrarDocumentoQuipuxStatus status);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Quipux registro: la radicación de la submission {SubmissionId} (trámite {ProcedureInstanceId}) no fue exitosa ({Status}: {Motivo}).")]
    public static partial void RadicarNoExitosa(
        ILogger logger, Guid submissionId, Guid procedureInstanceId,
        RegistrarDocumentoQuipuxStatus status, string motivo);

    [LoggerMessage(Level = LogLevel.Error,
        Message = "Quipux registro: falló la radicación de la submission {SubmissionId} (trámite {ProcedureInstanceId}); se reintentará en el próximo ciclo.")]
    public static partial void RadicarError(ILogger logger, Guid submissionId, Guid procedureInstanceId, Exception ex);

    [LoggerMessage(Level = LogLevel.Critical,
        Message = "Quipux registro: la submission {SubmissionId} (trámite {ProcedureInstanceId}) agotó los reintentos ({Attempts}) tras un fallo no controlado; NO se volverá a radicar. Requiere revisión manual.")]
    public static partial void DeadLettered(ILogger logger, Guid submissionId, Guid procedureInstanceId, int attempts);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Quipux registro: no se pudo cerrar la bitácora de la ejecución {RunId}.")]
    public static partial void JobRunLogError(ILogger logger, Guid runId, Exception ex);

    [LoggerMessage(Level = LogLevel.Error,
        Message = "Quipux registro: error en el ciclo; se reintentará.")]
    public static partial void CycleError(ILogger logger, Exception ex);
}
