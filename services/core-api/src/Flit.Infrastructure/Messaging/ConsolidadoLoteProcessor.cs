using Flit.Tramites.Application.UseCases.ConsolidadoLotes;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities.ConsolidadoLotes;
using Flit.Tramites.Domain.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Flit.Infrastructure.Messaging;

/// <summary>Ritmo del carril de ítems (los parámetros de negocio viven en <c>consolidado_export_settings</c>).</summary>
/// <param name="Sondeo">Espera entre ciclos cuando no hay nada que reclamar o todos los slots están ocupados.</param>
/// <param name="RetrasoInicial">Espera al arrancar el host (deja terminar migraciones y seeds).</param>
internal sealed record ConsolidadoLoteProcessorOptions(TimeSpan Sondeo, TimeSpan RetrasoInicial)
{
    public static ConsolidadoLoteProcessorOptions Predeterminadas { get; } =
        new(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10));
}

/// <summary>
/// HU #13376 (Épica #13216, ADR-0070 D2, CF-05/CF-20/CF-21) — carril de ítems del lote de descarga masiva de
/// consolidados: el carril de ítems, la transición <c>en_cola → en_proceso</c> y (HU #13377) el cierre del carril de
/// ítems de cada lote terminado (última parte + <c>en_proceso → empaquetando</c>) y (HU #13378) el carril de
/// empaquetado (1 slot) con la transición del lote a estado terminal.
/// <list type="bullet">
///   <item><b>Parámetros en BD</b>: cada ciclo lee <c>consolidado_export_settings</c>. Sin fila o con
///   <c>is_active = false</c> (decisión S4) no reclama nada y no toca ningún lote; al encenderse retoma los pendientes.
///   Si <c>item_lease_seconds</c> no es mayor que <c>item_timeout_seconds</c> el carril no arranca y lo registra
///   (una vez por cambio de estado, no en cada ciclo).</item>
///   <item><b>Tope</b>: como mucho <c>item_slots</c> ítems en ejecución en esta instancia (AC1).</item>
///   <item><b>Reclamo y equidad</b>: <see cref="IConsolidadoLoteRepository.ReclamarSiguienteItemAsync"/>, un ítem por
///   turno de lote (AC2). Un reclamo nunca abre una transacción larga: la generación ocurre fuera de transacción.</item>
///   <item><b>Timeout &lt; lease</b> (AC4): cada ítem corre con un token que vence a <c>item_timeout_seconds</c>;
///   el lease vence después, así que cuando el ítem vuelve a ser reclamable la ejecución anterior ya se canceló o el
///   proceso murió. El timeout cuenta como fallo técnico (<c>attempts + 1</c>: reintento u omisión
///   <c>error_tecnico</c> al agotar <c>max_item_attempts</c>), con el mismo puerto de cierre del handler.</item>
///   <item><b>Scope por ítem</b> (AC5): cada ítem resuelve <see cref="ProcesarItemLoteHandler"/> en un scope de DI
///   nuevo; el tenant del ítem viaja en el comando (<see cref="LoteItemContexto.CompaniaTramiteId"/>), nunca en estado
///   compartido.</item>
///   <item><b>Carril de empaquetado</b> (HU #13378): UN slot. Reclama una parte (<c>part_lease_seconds</c>) de un lote
///   <c>en_proceso</c> o <c>empaquetando</c> con <see cref="IConsolidadoLoteEmpaquetado.ReclamarSiguienteParteAsync"/> y
///   la entrega a <see cref="EmpaquetarParteHandler"/> en un scope nuevo, con un token que vence a
///   <c>part_timeout_seconds</c>. El timeout cuenta intento (<see cref="IConsolidadoLoteEmpaquetado.RegistrarFalloParteAsync"/>);
///   una parte re-reclamada con <c>max_part_attempts</c> ya agotado no vuelve a ejecutarse. Cada ciclo finaliza los
///   lotes listos (<see cref="IConsolidadoLoteEmpaquetado.FinalizarLoteAsync"/>). Con <c>part_lease_seconds</c> no
///   mayor que <c>part_timeout_seconds</c> solo se apaga este carril (se registra una vez); el de ítems sigue.</item>
///   <item><b>Reanudación</b> (AC3): una parada del host cancela los ítems en vuelo sin cerrarlos; el reclamo por lease
///   vencido los retoma (y cuenta esa ejecución sin cierre). Un ítem que llega con <c>attempts</c> agotado se omite con
///   <c>error_tecnico</c> sin volver a ejecutarse.</item>
/// </list>
/// Logs sin PII: ids de lote e ítem, códigos y conteos; nunca placa, radicado ni documento.
/// </summary>
/// <remarks>
/// Uso de ejemplo: <c>services.AddHostedService&lt;ConsolidadoLoteProcessor&gt;();</c> (registro en
/// <c>InfrastructureExtensions</c>).
/// </remarks>
internal sealed partial class ConsolidadoLoteProcessor : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ConsolidadoLoteProcessor> _logger;
    private readonly ConsolidadoLoteProcessorOptions _opciones;
    private readonly TimeProvider _time;
    private readonly string _instancia = $"{Environment.MachineName}/{Environment.ProcessId}";

    /// <summary>HU #13377 — lotes cuyo carril se cierra como mucho en un ciclo (el resto, en el siguiente).</summary>
    internal const int MaxCierresPorCiclo = 20;

    /// <summary>HU #13378 — lotes que se intentan finalizar como mucho en un ciclo.</summary>
    internal const int MaxFinalizacionesPorCiclo = 20;

    /// <summary>Ítems en ejecución. Solo lo toca el bucle de <see cref="ExecuteAsync"/>.</summary>
    private readonly List<Task> _enCurso = [];

    /// <summary>HU #13378 — la parte en empaquetado (1 slot). Solo la toca el bucle de <see cref="ExecuteAsync"/>.</summary>
    private Task? _empaquetado;

    /// <summary>HU #13378 — último estado registrado de los parámetros del carril de empaquetado (log una vez por cambio).</summary>
    private bool? _empaquetadoValido;

    private EstadoMotor _estado = EstadoMotor.Desconocido;

    public ConsolidadoLoteProcessor(IServiceScopeFactory scopeFactory, ILogger<ConsolidadoLoteProcessor> logger)
        : this(scopeFactory, logger, ConsolidadoLoteProcessorOptions.Predeterminadas, TimeProvider.System)
    {
    }

    internal ConsolidadoLoteProcessor(
        IServiceScopeFactory scopeFactory,
        ILogger<ConsolidadoLoteProcessor> logger,
        ConsolidadoLoteProcessorOptions opciones,
        TimeProvider timeProvider)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _opciones = opciones ?? throw new ArgumentNullException(nameof(opciones));
        _time = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    private enum EstadoMotor
    {
        Desconocido,
        Apagado,
        ParametrosInvalidos,
        Activo,
    }

    /// <summary>AC4 — invariante de no-solapamiento (D2) y mínimos del carril.</summary>
    internal static bool ParametrosValidos(ConsolidadoExportSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return settings.ItemSlots >= 1
            && settings.ItemTimeoutSeconds > 0
            && settings.ItemLeaseSeconds > settings.ItemTimeoutSeconds
            && settings.MaxItemAttempts >= 1;
    }

    /// <summary>HU #13378 — mismo invariante timeout &lt; lease para el carril de empaquetado, y al menos un intento.</summary>
    internal static bool ParametrosEmpaquetadoValidos(ConsolidadoExportSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return settings.PartTimeoutSeconds > 0
            && settings.PartLeaseSeconds > settings.PartTimeoutSeconds
            && settings.MaxPartAttempts >= 1;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            if (_opciones.RetrasoInicial > TimeSpan.Zero)
                await Task.Delay(_opciones.RetrasoInicial, _time, stoppingToken).ConfigureAwait(false);

            while (!stoppingToken.IsCancellationRequested)
            {
                _enCurso.RemoveAll(t => t.IsCompleted);
                try
                {
                    await DespacharAsync(stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
#pragma warning disable CA1031 // El carril no puede morir por un ciclo: se registra y sigue.
                catch (Exception ex)
#pragma warning restore CA1031
                {
                    // Solo el tipo: el mensaje de una excepción de BD puede citar valores de la fila.
                    LogCicloFallido(_logger, ex.GetType().Name);
                }

                // Despierta con el sondeo o en cuanto un slot (de ítems o de empaquetado) se libera.
                var sondeo = Task.Delay(_opciones.Sondeo, _time, stoppingToken);
                List<Task> despertadores = [.. _enCurso];
                if (_empaquetado is { IsCompleted: false } enVuelo)
                    despertadores.Add(enVuelo);
                if (despertadores.Count > 0)
                    await Task.WhenAny([.. despertadores, sondeo]).ConfigureAwait(false);
                else
                    await sondeo.ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Parada del host.
        }
        finally
        {
            // Los ítems en vuelo ven la cancelación por su token enlazado y no cierran: los retoma el reclamo por lease.
            await Task.WhenAll(_enCurso).ConfigureAwait(false);
            if (_empaquetado is not null)
                await _empaquetado.ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Un ciclo: lee los parámetros, pasa los lotes vacíos a <c>en_proceso</c> y reclama ítems hasta llenar los slots
    /// libres. Cada ítem reclamado se lanza en segundo plano.
    /// </summary>
    private async Task DespacharAsync(CancellationToken stoppingToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var repo = scope.ServiceProvider.GetRequiredService<IConsolidadoLoteRepository>();
        var settings = await repo.ObtenerSettingsAsync(stoppingToken).ConfigureAwait(false);
        if (!Habilitado(settings))
            return;

        // Hallazgo de #13373: un lote sin ítems no tiene nada que reclamar; queda en en_proceso para el cierre (#13377).
        await repo.IniciarLotesSinItemsAsync(stoppingToken).ConfigureAwait(false);

        // HU #13377: los lotes cuyo carril de ítems terminó reciben su última parte y pasan a empaquetando.
        await CerrarCarrilesTerminadosAsync(repo, stoppingToken).ConfigureAwait(false);

        // HU #13378: transición terminal de los lotes listos y, si el slot está libre, la siguiente parte. Aislado: un
        // fallo del carril de empaquetado nunca frena el de ítems. El puerto se resuelve opcional porque los hosts de
        // prueba del carril de ítems no lo registran; en core-api lo garantiza CoreApiServiceRegistrationSnapshotTests.
        if (EmpaquetadoHabilitado(settings!)
            && scope.ServiceProvider.GetService<IConsolidadoLoteEmpaquetado>() is { } empaquetado)
        {
            try
            {
                await DespacharEmpaquetadoAsync(empaquetado, settings!, stoppingToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogCicloEmpaquetadoFallido(_logger, ex.GetType().Name);
            }
        }

        while (_enCurso.Count(t => !t.IsCompleted) < settings!.ItemSlots)
        {
            var reclamado = await repo
                .ReclamarSiguienteItemAsync(_instancia, settings.ItemLeaseSeconds, stoppingToken)
                .ConfigureAwait(false);
            if (reclamado is null)
                return;

            LogReclamado(_logger, reclamado.Lote.Id, reclamado.Item.Id, reclamado.Item.Attempts);
            _enCurso.Add(Task.Run(() => ProcesarAsync(reclamado, settings, stoppingToken), CancellationToken.None));
        }
    }

    /// <summary>
    /// HU #13377 — cierra el carril de cada lote <c>en_proceso</c> sin ítems vivos (hasta
    /// <see cref="MaxCierresPorCiclo"/> por ciclo). Cada cierre es su propia transacción con el lock del lote; un fallo
    /// en un lote se registra (solo id y tipo) y no frena a los demás: el ciclo siguiente lo reintenta.
    /// </summary>
    private async Task CerrarCarrilesTerminadosAsync(IConsolidadoLoteRepository repo, CancellationToken stoppingToken)
    {
        var terminados = await repo.ObtenerLotesConCarrilTerminadoAsync(MaxCierresPorCiclo, stoppingToken).ConfigureAwait(false);
        foreach (var loteId in terminados)
        {
            try
            {
                var cierre = await repo.CerrarCarrilAsync(loteId, stoppingToken).ConfigureAwait(false);
                if (cierre.Aplicado)
                    LogCarrilCerrado(_logger, loteId, cierre.PartesCreadas, cierre.PartesTotales);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogCierreCarrilFallido(_logger, loteId, ex.GetType().Name);
            }
        }
    }

    /// <summary>HU #13378 — un ciclo del carril de empaquetado: finaliza lo listo y, con el slot libre, reclama una parte.</summary>
    private async Task DespacharEmpaquetadoAsync(
        IConsolidadoLoteEmpaquetado empaquetado, ConsolidadoExportSettings settings, CancellationToken stoppingToken)
    {
        await FinalizarListosAsync(empaquetado, stoppingToken).ConfigureAwait(false);
        if (_empaquetado is { IsCompleted: false })
            return;

        _empaquetado = null;
        var parte = await empaquetado.ReclamarSiguienteParteAsync(settings.PartLeaseSeconds, stoppingToken).ConfigureAwait(false);
        if (parte is null)
            return;

        LogParteReclamada(_logger, parte.Lote.Id, parte.Parte.PartNumber, parte.Parte.Attempts);
        _empaquetado = Task.Run(() => EmpaquetarAsync(parte, settings, stoppingToken), CancellationToken.None);
    }

    /// <summary>
    /// HU #13378 — finaliza los lotes listos (hasta <see cref="MaxFinalizacionesPorCiclo"/>). Un fallo en un lote se
    /// registra y no frena a los demás: el ciclo siguiente lo reintenta.
    /// </summary>
    private async Task FinalizarListosAsync(IConsolidadoLoteEmpaquetado empaquetado, CancellationToken stoppingToken)
    {
        var listos = await empaquetado.ObtenerLotesParaFinalizarAsync(MaxFinalizacionesPorCiclo, stoppingToken).ConfigureAwait(false);
        foreach (var loteId in listos)
        {
            try
            {
                var finalizado = await empaquetado.FinalizarLoteAsync(loteId, stoppingToken).ConfigureAwait(false);
                if (finalizado is not null)
                    LogLoteFinalizado(_logger, loteId, finalizado.Estado);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogFinalizacionFallida(_logger, loteId, ex.GetType().Name);
            }
        }
    }

    private bool EmpaquetadoHabilitado(ConsolidadoExportSettings settings)
    {
        var valido = ParametrosEmpaquetadoValidos(settings);
        if (valido != _empaquetadoValido)
        {
            _empaquetadoValido = valido;
            if (valido)
                LogEmpaquetadoActivo(_logger, settings.PartTimeoutSeconds, settings.PartLeaseSeconds, settings.MaxPartAttempts);
            else
                LogEmpaquetadoParametrosInvalidos(_logger, settings.PartTimeoutSeconds, settings.PartLeaseSeconds, settings.MaxPartAttempts);
        }

        return valido;
    }

    /// <summary>
    /// HU #13378 — empaqueta una parte reclamada. Nunca lanza. El timeout (<c>part_timeout_seconds</c>) cuenta intento;
    /// la parada del host no (la retoma el reclamo por lease, que sí lo cuenta al re-reclamar).
    /// </summary>
    private async Task EmpaquetarAsync(ParteLoteReclamada reclamada, ConsolidadoExportSettings settings, CancellationToken stoppingToken)
    {
        var (lote, parte) = (reclamada.Lote, reclamada.Parte);
        try
        {
            if (parte.Attempts >= settings.MaxPartAttempts)
            {
                // Re-reclamada tras max_part_attempts ejecuciones sin cierre: no se vuelve a ejecutar.
                await RegistrarFalloParteAsync(reclamada, parte.Attempts, settings, stoppingToken).ConfigureAwait(false);
                return;
            }

            using var porTiempo = new CancellationTokenSource(TimeSpan.FromSeconds(settings.PartTimeoutSeconds), _time);
            using var enlazado = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, porTiempo.Token);
            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                var handler = scope.ServiceProvider.GetRequiredService<EmpaquetarParteHandler>();
                var desenlace = await handler.HandleAsync(reclamada, settings.MaxPartAttempts, enlazado.Token).ConfigureAwait(false);
                LogParteEmpaquetada(_logger, lote.Id, parte.PartNumber, desenlace);
            }
            catch (OperationCanceledException) when (porTiempo.IsCancellationRequested && !stoppingToken.IsCancellationRequested)
            {
                LogParteTimeout(_logger, lote.Id, parte.PartNumber, settings.PartTimeoutSeconds);
                await RegistrarFalloParteAsync(reclamada, (short)(parte.Attempts + 1), settings, stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            LogParteInterrumpida(_logger, lote.Id, parte.PartNumber);
        }
#pragma warning disable CA1031 // Una parte fallida no tumba el carril: queda empaquetando y la retoma el reclamo por lease.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            LogParteFallida(_logger, lote.Id, parte.PartNumber, ex.GetType().Name);
        }
    }

    private async Task RegistrarFalloParteAsync(
        ParteLoteReclamada reclamada, short intentos, ConsolidadoExportSettings settings, CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var empaquetado = scope.ServiceProvider.GetRequiredService<IConsolidadoLoteEmpaquetado>();
        var desenlace = await empaquetado.RegistrarFalloParteAsync(
            new FalloParteLote(reclamada.Lote.Id, reclamada.Parte.PartNumber, reclamada.Parte.Attempts, intentos, settings.MaxPartAttempts),
            ct).ConfigureAwait(false);
        LogFalloParteRegistrado(_logger, reclamada.Lote.Id, reclamada.Parte.PartNumber, intentos, settings.MaxPartAttempts, desenlace);
    }

    private bool Habilitado(ConsolidadoExportSettings? settings)
    {
        EstadoMotor nuevo;
        if (settings is null || !settings.IsActive)
            nuevo = EstadoMotor.Apagado;
        else if (!ParametrosValidos(settings))
            nuevo = EstadoMotor.ParametrosInvalidos;
        else
            nuevo = EstadoMotor.Activo;

        if (nuevo != _estado)
        {
            _estado = nuevo;
            switch (nuevo)
            {
                case EstadoMotor.Apagado:
                    LogApagado(_logger);
                    break;
                case EstadoMotor.ParametrosInvalidos:
                    LogParametrosInvalidos(_logger, settings!.ItemTimeoutSeconds, settings.ItemLeaseSeconds,
                        settings.ItemSlots, settings.MaxItemAttempts);
                    break;
                default:
                    LogActivo(_logger, settings!.ItemSlots, settings.ItemTimeoutSeconds, settings.ItemLeaseSeconds);
                    break;
            }
        }

        return nuevo == EstadoMotor.Activo;
    }

    /// <summary>Procesa un ítem reclamado. Nunca lanza: el carril sigue con el siguiente.</summary>
    private async Task ProcesarAsync(ItemLoteReclamado reclamado, ConsolidadoExportSettings settings, CancellationToken stoppingToken)
    {
        var (lote, item) = (reclamado.Lote, reclamado.Item);
        try
        {
            if (item.Attempts >= settings.MaxItemAttempts)
            {
                // Re-reclamado tras max_item_attempts ejecuciones sin cierre: no se vuelve a ejecutar.
                await CerrarPorFalloAsync(lote, item, item.Attempts, settings, stoppingToken).ConfigureAwait(false);
                return;
            }

            using var porTiempo = new CancellationTokenSource(TimeSpan.FromSeconds(settings.ItemTimeoutSeconds), _time);
            using var enlazado = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, porTiempo.Token);
            try
            {
                // AC5 — scope de DI nuevo por ítem; el tenant del ítem viaja en el comando.
                await using var scope = _scopeFactory.CreateAsyncScope();
                var handler = scope.ServiceProvider.GetRequiredService<ProcesarItemLoteHandler>();
                var resultado = await handler
                    .HandleAsync(ProcesarItemLoteCommand.Con(lote, item, settings), enlazado.Token)
                    .ConfigureAwait(false);
                if (!resultado.Aplicado)
                    LogCierreNoAplicado(_logger, lote.Id, item.Id, resultado.Desenlace);
            }
            catch (OperationCanceledException) when (porTiempo.IsCancellationRequested && !stoppingToken.IsCancellationRequested)
            {
                LogTimeout(_logger, lote.Id, item.Id, settings.ItemTimeoutSeconds);
                await CerrarPorFalloAsync(lote, item, (short)(item.Attempts + 1), settings, stoppingToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Solo propagan la cancelación y la falta de procesador para el origen (configuración).
                LogItemFallido(_logger, lote.Id, item.Id, ex.GetType().Name);
                await CerrarPorFalloAsync(lote, item, (short)(item.Attempts + 1), settings, stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            LogInterrumpido(_logger, lote.Id, item.Id);
        }
#pragma warning disable CA1031 // Un ítem fallido no tumba el carril: queda procesando y lo retoma el reclamo por lease.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            LogItemFallido(_logger, lote.Id, item.Id, ex.GetType().Name);
        }
    }

    /// <summary>
    /// Fallo sin desenlace del handler (timeout, excepción o intentos agotados por caídas): reintento con
    /// <c>retry_delay_seconds</c> o, al llegar a <c>max_item_attempts</c>, omisión <c>error_tecnico</c>. Mismo puerto y
    /// misma condición (<c>procesando</c> con lease vigente) que el cierre del handler.
    /// </summary>
    private async Task CerrarPorFalloAsync(
        ConsolidadoExportBatch lote, ConsolidadoExportBatchItem item, short intentos, ConsolidadoExportSettings settings,
        CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var proceso = scope.ServiceProvider.GetRequiredService<IConsolidadoLoteItemProceso>();
        var ahora = _time.GetUtcNow();
        bool aplicado;
        if (intentos >= settings.MaxItemAttempts)
        {
            var codigo = ConsolidadoLoteOmisiones.ErrorTecnico;
            aplicado = await proceso
                .MarcarOmitidoAsync(new LoteItemOmitido(lote.Id, item.Id, codigo, ConsolidadoErrorTextos.ParaLote(codigo), intentos, ahora), ct)
                .ConfigureAwait(false);
        }
        else
        {
            aplicado = await proceso
                .ReprogramarAsync(new LoteItemReintento(lote.Id, item.Id, intentos, ahora.AddSeconds(settings.RetryDelaySeconds)), ct)
                .ConfigureAwait(false);
        }

        LogFalloCerrado(_logger, lote.Id, item.Id, intentos, settings.MaxItemAttempts, aplicado);
    }

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Carril de ítems de lotes: motor apagado o sin parámetros; no se reclama ningún ítem.")]
    private static partial void LogApagado(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error,
        Message = "Carril de ítems de lotes NO arranca: parámetros incoherentes (item_timeout_seconds={Timeout}, item_lease_seconds={Lease}, item_slots={Slots}, max_item_attempts={MaxIntentos}); el lease debe ser mayor que el timeout.")]
    private static partial void LogParametrosInvalidos(ILogger logger, int timeout, int lease, short slots, short maxIntentos);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Carril de ítems de lotes activo: item_slots={Slots}, item_timeout_seconds={Timeout}, item_lease_seconds={Lease}.")]
    private static partial void LogActivo(ILogger logger, short slots, int timeout, int lease);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Lote {BatchId}: ítem {ItemId} reclamado (intentos previos {Intentos}).")]
    private static partial void LogReclamado(ILogger logger, Guid batchId, Guid itemId, short intentos);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Lote {BatchId}: el cierre del ítem {ItemId} ({Desenlace}) no se aplicó (lote cancelado, ítem cerrado o reserva vencida).")]
    private static partial void LogCierreNoAplicado(
        ILogger logger, Guid batchId, Guid itemId, ProcesarItemLoteDesenlace desenlace);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Lote {BatchId}: ítem {ItemId} cancelado por timeout ({TimeoutSegundos} s).")]
    private static partial void LogTimeout(ILogger logger, Guid batchId, Guid itemId, int timeoutSegundos);

    [LoggerMessage(Level = LogLevel.Error, Message = "Lote {BatchId}: el ítem {ItemId} lanzó {ExceptionType}.")]
    private static partial void LogItemFallido(ILogger logger, Guid batchId, Guid itemId, string exceptionType);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Lote {BatchId}: ítem {ItemId} cerrado por fallo técnico, intento {Intentos} de {MaxIntentos}; aplicado={Aplicado}.")]
    private static partial void LogFalloCerrado(
        ILogger logger, Guid batchId, Guid itemId, short intentos, short maxIntentos, bool aplicado);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Lote {BatchId}: ítem {ItemId} interrumpido por la parada del host; lo retoma el reclamo por lease.")]
    private static partial void LogInterrumpido(ILogger logger, Guid batchId, Guid itemId);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Lote {BatchId}: carril de ítems cerrado; {PartesCreadas} parte(s) nueva(s), {PartesTotales} en total; pasa a empaquetando.")]
    private static partial void LogCarrilCerrado(ILogger logger, Guid batchId, int partesCreadas, int partesTotales);

    [LoggerMessage(Level = LogLevel.Error,
        Message = "Lote {BatchId}: el cierre del carril de ítems lanzó {ExceptionType}; se reintentará en el ciclo siguiente.")]
    private static partial void LogCierreCarrilFallido(ILogger logger, Guid batchId, string exceptionType);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Carril de empaquetado de lotes activo: part_timeout_seconds={Timeout}, part_lease_seconds={Lease}, max_part_attempts={MaxIntentos}.")]
    private static partial void LogEmpaquetadoActivo(ILogger logger, int timeout, int lease, short maxIntentos);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Carril de empaquetado de lotes NO arranca: parámetros incoherentes (part_timeout_seconds={Timeout}, part_lease_seconds={Lease}, max_part_attempts={MaxIntentos}); el lease debe ser mayor que el timeout.")]
    private static partial void LogEmpaquetadoParametrosInvalidos(ILogger logger, int timeout, int lease, short maxIntentos);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Lote {BatchId}: parte {PartNumber} reclamada (intentos previos {Intentos}).")]
    private static partial void LogParteReclamada(ILogger logger, Guid batchId, short partNumber, short intentos);

    [LoggerMessage(Level = LogLevel.Information, Message = "Lote {BatchId}: parte {PartNumber} → {Desenlace}.")]
    private static partial void LogParteEmpaquetada(ILogger logger, Guid batchId, short partNumber, EmpaquetarParteDesenlace desenlace);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Lote {BatchId}: parte {PartNumber} cancelada por timeout ({TimeoutSegundos} s).")]
    private static partial void LogParteTimeout(ILogger logger, Guid batchId, short partNumber, int timeoutSegundos);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Lote {BatchId}: parte {PartNumber} interrumpida por la parada del host; la retoma el reclamo por lease.")]
    private static partial void LogParteInterrumpida(ILogger logger, Guid batchId, short partNumber);

    [LoggerMessage(Level = LogLevel.Error, Message = "Lote {BatchId}: el empaquetado de la parte {PartNumber} lanzó {ExceptionType}.")]
    private static partial void LogParteFallida(ILogger logger, Guid batchId, short partNumber, string exceptionType);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Lote {BatchId}: parte {PartNumber} con fallo técnico, intento {Intentos} de {MaxIntentos} → {Desenlace}.")]
    private static partial void LogFalloParteRegistrado(
        ILogger logger, Guid batchId, short partNumber, short intentos, short maxIntentos, FalloParteDesenlace desenlace);

    [LoggerMessage(Level = LogLevel.Error,
        Message = "Carril de empaquetado de lotes: falló un ciclo ({ExceptionType}); se reintentará. El carril de ítems sigue.")]
    private static partial void LogCicloEmpaquetadoFallido(ILogger logger, string exceptionType);

    [LoggerMessage(Level = LogLevel.Information, Message = "Lote {BatchId}: finalizado en {Estado}.")]
    private static partial void LogLoteFinalizado(ILogger logger, Guid batchId, string estado);

    [LoggerMessage(Level = LogLevel.Error,
        Message = "Lote {BatchId}: la transición terminal lanzó {ExceptionType}; se reintentará en el ciclo siguiente.")]
    private static partial void LogFinalizacionFallida(ILogger logger, Guid batchId, string exceptionType);

    [LoggerMessage(Level = LogLevel.Error, Message = "Carril de ítems de lotes: falló un ciclo ({ExceptionType}); se reintentará.")]
    private static partial void LogCicloFallido(ILogger logger, string exceptionType);
}
