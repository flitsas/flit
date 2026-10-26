using Flit.Diagnostico.Api.Modulos.Consultas;
using Flit.Diagnostico.Api.Modulos.Tablero;
using Flit.Diagnostico.Api.Persistence;
using Flit.Diagnostico.Domain.Consultas;
using Flit.Platform.Sdk.Messaging;
using Microsoft.Extensions.Options;

namespace Flit.Diagnostico.Api.Modulos.Trabajos;

/// <summary>
/// Procesador genérico de la cola de celdas (HU #13727, ADR a): una vuelta toma hasta <see cref="OpcionesProcesador.TamanoLote"/>
/// celdas con <see cref="ColaConsultas"/> (solo de las fuentes con un <see cref="IEjecutorCelda"/> registrado; sin ninguno no
/// toma nada), las ejecuta en paralelo y cierra cada una con <see cref="EscritorCeldas"/>. Cada ejecución corre en su propio scope
/// y fija ahí la empresa de su celda (<see cref="Plataforma.EmpresaDelTrabajo"/>, ADR-0075) ANTES de leer o escribir: el
/// <c>DiagnosticoDb</c>, la outbox y el publicador de celdas de ese scope la comparten. Si el proceso se cancela (el pod se detiene) la celda queda
/// EN_CURSO con su lease y otra réplica la retoma al vencer; un fallo inesperado del ejecutor la deja en ERROR (definitivo).
/// Cada ejecución deja un intento auditable (<see cref="RegistroIntentos"/>, HU #13737) y un error transitorio con reintentos
/// disponibles vuelve a la cola con backoff (<see cref="PoliticaReintentos"/>, <c>Diagnostico:Consultas:MaxReintentos</c>, D-12). Al cerrar la RUNT de un vehículo, <see cref="OrquestadorVehiculo"/> libera
/// o bloquea sus demás celdas en la misma transacción (HU #13730), y tras toda celda terminal <see cref="RecalculadorEstado"/>
/// recalcula el estado global del lote en esa misma transacción (HU #13743). Antes de llamar al ejecutor, <see cref="ReutilizacionMismoDia"/>
/// copia el dato de hoy de la misma compañía, placa y fuente si existe (HU #13739, D-10): no se llama a la fuente y la celda se
/// cierra por el mismo camino, con su intento <c>desde_cache=true</c>; un reproceso (HU #13746) no reutiliza: repite la consulta real. Si hay más de un ejecutor para una fuente, gana el primero
/// registrado. Los logs llevan ids, fuente y tipo de error, nunca datos de la fuente.
/// </summary>
internal sealed partial class ProcesadorConsultas(
    IServiceScopeFactory scopes, IOptions<OpcionesProcesador> opciones, IOptions<OpcionesConsultas> consultas, TimeProvider reloj,
    ILogger<ProcesadorConsultas> log)
{
    private readonly PoliticaReintentos _politica = new(consultas.Value.MaxReintentos);

    /// <summary>Toma un lote, lo ejecuta y devuelve cuántas celdas tomó (0 si la cola no tenía nada tomable).</summary>
    public async Task<int> ProcesarLoteAsync(CancellationToken ct)
    {
        var o = opciones.Value;
        IReadOnlyList<CeldaTomada> tomadas;
        await using (var scope = scopes.CreateAsyncScope())
        {
            var fuentes = scope.ServiceProvider.GetServices<IEjecutorCelda>().Select(e => e.Fuente).Distinct().ToList();
            if (fuentes.Count == 0)
                return 0;
            tomadas = await scope.ServiceProvider.GetRequiredService<ColaConsultas>()
                .TomarAsync(o.TamanoLote, o.DuracionLease, o.VehiculosEnVueloPorEmpresa, fuentes, ct).ConfigureAwait(false);
        }

        if (tomadas.Count == 0)
            return 0;
        await Task.WhenAll(tomadas.Select(c => EjecutarAsync(c, ct))).ConfigureAwait(false);
        return tomadas.Count;
    }

    private async Task EjecutarAsync(CeldaTomada celda, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        // ADR-0075: la empresa de la celda queda fijada en el scope; el contexto y la outbox del scope la usan.
        scope.ServiceProvider.GetRequiredService<Plataforma.EmpresaDelTrabajo>().Fijar(celda.TenantId);
        // Gana el primero registrado para la fuente: las pruebas ponen su doble antes que Program (como con TryAdd).
        var ejecutor = scope.ServiceProvider.GetServices<IEjecutorCelda>().First(e => e.Fuente == celda.Fuente);
        var db = scope.ServiceProvider.GetRequiredService<DiagnosticoDb>();

        ResultadoCelda resultado;
        var inicio = reloj.GetUtcNow();
        try
        {
            // HU #13739 (D-10): si la misma compañía ya tiene hoy el dato de esa placa y fuente, se copia sin llamar ni consumir.
            var contexto = new ContextoCelda(celda, db);
            // HU #13746 (R-1): el reproceso repite la consulta real, nunca copia el dato de hoy.
            var reutilizado = celda.ReprocesoUsuarioId is null
                ? await ReutilizacionMismoDia.BuscarAsync(contexto, inicio, ct).ConfigureAwait(false)
                : null;
            if (reutilizado is not null)
                Reutilizada(log, celda.Id, celda.Fuente, reutilizado.ReutilizadaDeId!.Value);
            resultado = reutilizado ?? await ejecutor.EjecutarAsync(contexto, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // El proceso se detiene: no se toca la celda; su lease vence y otra réplica la retoma.
            throw;
        }
#pragma warning disable CA1031 // Un ejecutor que falla no tumba la vuelta: su celda queda en ERROR.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            EjecucionFallida(log, celda.Id, celda.Fuente, ex.GetType().Name);
            resultado = ResultadoCelda.Error() with { Intento = new DatosIntento(null, null, null, $"Falla inesperada del ejecutor ({ex.GetType().Name}).") };
        }

        // HU #13737: un intento por ejecución; un transitorio con reintentos disponibles vuelve a la cola con backoff.
        var fin = reloj.GetUtcNow();
        // HU #13792: una celda de persona deja un intento por persona consultada.
        var intentos = RegistroIntentos.CrearTodos(celda, inicio, fin, resultado);
        if (resultado.Estado == EstadoCelda.Error)
        {
            var decision = _politica.Decidir(resultado.TipoError, celda.IntentosAuto, fin);
            if (decision.Reintentar)
            {
                resultado = resultado with { Estado = EstadoCelda.Pendiente, ReintentarDesde = decision.DisponibleDesde };
                Reintento(log, celda.Id, celda.Fuente, resultado.TipoError ?? "-", decision.IntentosAuto, _politica.MaxReintentos);
            }
            else if (resultado.TipoErrorSinReintento is { } final)
            {
                // HU #13792: sin reintento, la celda de persona queda con su código final (PERSONAS_CON_ERROR o la causa común).
                resultado = resultado with { TipoError = final };
            }
        }

        // RUNT va primero (HU #13730): su cierre libera o bloquea las demás celdas del vehículo en la misma transacción (y las
        // liberadas publican su trabajo con el publicador del scope, ADR-0075). Un reintento no es un cierre: las demás siguen esperando.
        resultado = OrquestadorVehiculo.TrasEjecutar(celda, resultado, reloj, scope.ServiceProvider.GetRequiredService<Consultas.Bus.PublicadorCeldas>());
        // HU #13743: tras cada celda terminal, el estado global del lote se recalcula en la misma transacción de cierre (y, si
        // queda terminado, diagnostico.lote.terminado sale por la outbox del scope, ADR-0075).
        resultado = RecalculadorEstado.TrasCerrar(celda, resultado, reloj, scope.ServiceProvider.GetRequiredService<IPlatformOutbox>());
        if (!await new EscritorCeldas(db, reloj).CerrarAsync(celda, resultado, ct, intentos).ConfigureAwait(false))
        {
            LeasePerdido(log, celda.Id, celda.Fuente);
            await RegistroIntentos.GuardarSoloAsync(db, intentos, ct).ConfigureAwait(false);
        }
    }

    [LoggerMessage(EventId = 137270, Level = LogLevel.Warning, Message = "La celda {CeldaId} de {Fuente} falló en su ejecutor ({TipoError}) y queda en ERROR")]
    private static partial void EjecucionFallida(ILogger logger, Guid celdaId, Fuente fuente, string tipoError);

    [LoggerMessage(EventId = 137271, Level = LogLevel.Warning, Message = "La celda {CeldaId} de {Fuente} cambió de lease mientras se ejecutaba: no se cierra")]
    private static partial void LeasePerdido(ILogger logger, Guid celdaId, Fuente fuente);

    [LoggerMessage(EventId = 137390, Level = LogLevel.Information, Message = "La celda {CeldaId} de {Fuente} reutilizó sin consumo el dato del mismo día de la celda {Original}")]
    private static partial void Reutilizada(ILogger logger, Guid celdaId, Fuente fuente, Guid original);

    [LoggerMessage(EventId = 137370, Level = LogLevel.Information, Message = "La celda {CeldaId} de {Fuente} terminó en {TipoError} (transitorio): reintento {Reintento} de {Maximo} con backoff")]
    private static partial void Reintento(ILogger logger, Guid celdaId, Fuente fuente, string tipoError, int reintento, int maximo);
}
