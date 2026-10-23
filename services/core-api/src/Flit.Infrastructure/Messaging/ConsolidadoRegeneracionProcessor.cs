using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Flit.Infrastructure.Messaging;

/// <summary>
/// HU #12795 (Épica #12760, D1) — worker de la regeneración anticipada de consolidados.
///
/// <para>Su única responsabilidad es <b>el ritmo</b>: drenar el canal
/// (<see cref="ChannelConsolidadoRegeneracionQueue"/>), fundir solicitudes por clave con
/// <see cref="ConsolidadoRegeneracionDebouncer"/> y, al vencer la ventana, ejecutar el trabajo. La
/// decisión de regenerar y la generación del PDF viven en
/// <see cref="RegenerarConsolidadoAnticipadoHandler"/> (Application), que delega en los handlers de
/// siempre.</para>
///
/// <para><b>Tenant (AC5).</b> Cada trabajo corre en su PROPIO scope de DI y recibe el tenant de la
/// solicitud: un <c>BackgroundService</c> es singleton y no puede sostener un <c>DbContext</c> scoped, y
/// compartir scope entre trabajos de tenants distintos arrastraría el change tracker de uno al otro.</para>
///
/// <para>Los trabajos se ejecutan en secuencia: componer PDF es costoso y no hay requisito de latencia
/// (el camino perezoso sirve al usuario que llegue antes).</para>
/// </summary>
internal sealed class ConsolidadoRegeneracionProcessor : BackgroundService
{
    private readonly ChannelConsolidadoRegeneracionQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ConsolidadoRegeneracionProcessor> _logger;
    private readonly ConsolidadoRegeneracionDebouncer _debouncer;

    public ConsolidadoRegeneracionProcessor(
        ChannelConsolidadoRegeneracionQueue queue,
        IServiceScopeFactory scopeFactory,
        IOptions<ConsolidadoRegeneracionOptions> options,
        TimeProvider timeProvider,
        ILogger<ConsolidadoRegeneracionProcessor> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        _queue = queue ?? throw new ArgumentNullException(nameof(queue));
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _debouncer = new ConsolidadoRegeneracionDebouncer(options.Value.VentanaDebounce, options.Value.EsperaMaxima);
    }

    /// <summary>Claves en espera de que venza su ventana (diagnóstico y tests).</summary>
    internal int Pendientes => _debouncer.Pendientes;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // La lectura pendiente se conserva entre vueltas: pedir un WaitToReadAsync nuevo en cada
        // iteración dejaría esperas huérfanas sobre el canal cada vez que gana el temporizador.
        Task<bool>? lectura = null;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                DrenarCola();
                await ProcesarVencidasAsync(stoppingToken).ConfigureAwait(false);

                lectura ??= _queue.WaitToReadAsync(stoppingToken).AsTask();
                var espera = _debouncer.TiempoHastaProximo(_timeProvider.GetUtcNow());

                if (espera is { } falta)
                    await Task.WhenAny(lectura, Task.Delay(falta, _timeProvider, stoppingToken)).ConfigureAwait(false);
                else
                    await lectura.ConfigureAwait(false);

                if (lectura.IsCompleted)
                {
                    if (!await lectura.ConfigureAwait(false))
                        break; // Canal completado: no llegarán más solicitudes.
                    lectura = null;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
#pragma warning disable CA1031 // El worker no puede morir por un ciclo: se registra y sigue.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                ConsolidadoRegeneracionLog.CicloFallido(_logger, ex);
                lectura = null;
            }
        }
    }

    /// <summary>Pasa lo encolado al debouncer con la hora actual del <see cref="TimeProvider"/>.</summary>
    internal int DrenarCola()
    {
        var leidas = 0;
        var ahora = _timeProvider.GetUtcNow();
        while (_queue.TryDequeue(out var solicitud))
        {
            _debouncer.Registrar(solicitud, ahora);
            leidas++;
        }

        return leidas;
    }

    /// <summary>Ejecuta las claves cuya ventana venció. Devuelve cuántos trabajos se ejecutaron.</summary>
    internal async Task<int> ProcesarVencidasAsync(CancellationToken ct)
    {
        var vencidas = _debouncer.TomarVencidas(_timeProvider.GetUtcNow());
        foreach (var solicitud in vencidas)
        {
            ct.ThrowIfCancellationRequested();
            await EjecutarAsync(solicitud, ct).ConfigureAwait(false);
        }

        return vencidas.Count;
    }

    private async Task EjecutarAsync(SolicitudRegeneracion solicitud, CancellationToken ct)
    {
        try
        {
            // AC5 — scope nuevo por trabajo; el tenant viaja explícito a todas las lecturas.
            await using var scope = _scopeFactory.CreateAsyncScope();
            var handler = scope.ServiceProvider.GetRequiredService<RegenerarConsolidadoAnticipadoHandler>();
            await handler
                .HandleAsync(solicitud.TenantId, solicitud.ProcedureInstanceId, solicitud.Documento, ct)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
#pragma warning disable CA1031 // Un trabajo fallido no tumba a los demás: se registra y se sigue.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            ConsolidadoRegeneracionLog.TrabajoFallido(
                _logger, ex, solicitud.Documento, solicitud.ProcedureInstanceId, solicitud.TenantId);
        }
    }
}

/// <summary>Logging source-generated (CA1848) de la cola. Solo ids: sin PII del trámite.</summary>
internal static partial class ConsolidadoRegeneracionLog
{
    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Cola de regeneración anticipada llena: se descarta {Documento} (instancia {InstanceId}, tenant {TenantId}); lo cubre el camino perezoso.")]
    public static partial void ColaLlena(ILogger logger, TipoConsolidado documento, Guid instanceId, Guid tenantId);

    [LoggerMessage(Level = LogLevel.Error,
        Message = "Regeneración anticipada del consolidado {Documento} lanzó una excepción (instancia {InstanceId}, tenant {TenantId}); se conserva el PDF anterior.")]
    public static partial void TrabajoFallido(
        ILogger logger, Exception ex, TipoConsolidado documento, Guid instanceId, Guid tenantId);

    [LoggerMessage(Level = LogLevel.Error,
        Message = "Cola de regeneración anticipada: falló un ciclo del worker; se reintentará.")]
    public static partial void CicloFallido(ILogger logger, Exception ex);
}
