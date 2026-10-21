using Flit.Diagnostico.Api.Modulos.Consultas;
using Flit.Diagnostico.Api.Persistence;
using Flit.Diagnostico.Domain.Consultas;
using Flit.Platform.Sdk.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Flit.Diagnostico.Api.Modulos.Trabajos;

/// <summary>
/// Procesador genérico de la cola de celdas (HU #13727, ADR a): una vuelta toma hasta <see cref="OpcionesProcesador.TamanoLote"/>
/// celdas con <see cref="ColaConsultas"/> (solo de las fuentes con un <see cref="IEjecutorCelda"/> registrado; sin ninguno no
/// toma nada), las ejecuta en paralelo y cierra cada una con <see cref="EscritorCeldas"/>. Cada ejecución fija la empresa de
/// su celda en un contexto propio ANTES de leer o escribir. Si el proceso se cancela (el pod se detiene) la celda queda
/// EN_CURSO con su lease y otra réplica la retoma al vencer; un fallo inesperado del ejecutor la deja en ERROR (los
/// reintentos de errores transitorios son de F03-H6). Al cerrar la RUNT de un vehículo, <see cref="OrquestadorVehiculo"/> libera
/// o bloquea sus demás celdas en la misma transacción (HU #13730). Si hay más de un ejecutor para una fuente, gana el primero
/// registrado. Los logs llevan ids, fuente y tipo de error, nunca datos de la fuente.
/// </summary>
internal sealed partial class ProcesadorConsultas(
    IServiceScopeFactory scopes, IOptions<OpcionesProcesador> opciones, TimeProvider reloj, ILogger<ProcesadorConsultas> log)
{
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
        // Gana el primero registrado para la fuente: las pruebas ponen su doble antes que Program (como con TryAdd).
        var ejecutor = scope.ServiceProvider.GetServices<IEjecutorCelda>().First(e => e.Fuente == celda.Fuente);
        await using var db = new DiagnosticoDb(
            scope.ServiceProvider.GetRequiredService<DbContextOptions<DiagnosticoDb>>(), new EmpresaDeLaCelda(celda.TenantId));

        ResultadoCelda resultado;
        try
        {
            resultado = await ejecutor.EjecutarAsync(new ContextoCelda(celda, db), ct).ConfigureAwait(false);
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
            resultado = ResultadoCelda.Error();
        }

        // RUNT va primero (HU #13730): su cierre libera o bloquea las demás celdas del vehículo en la misma transacción.
        resultado = OrquestadorVehiculo.TrasEjecutar(celda, resultado, reloj);
        if (!await new EscritorCeldas(db, reloj).CerrarAsync(celda, resultado, ct).ConfigureAwait(false))
            LeasePerdido(log, celda.Id, celda.Fuente);
    }

    [LoggerMessage(EventId = 137270, Level = LogLevel.Warning, Message = "La celda {CeldaId} de {Fuente} falló en su ejecutor ({TipoError}) y queda en ERROR")]
    private static partial void EjecucionFallida(ILogger logger, Guid celdaId, Fuente fuente, string tipoError);

    [LoggerMessage(EventId = 137271, Level = LogLevel.Warning, Message = "La celda {CeldaId} de {Fuente} cambió de lease mientras se ejecutaba: no se cierra")]
    private static partial void LeasePerdido(ILogger logger, Guid celdaId, Fuente fuente);

    /// <summary>La empresa de la celda en ejecución: el contexto filtra y valida con ella, como una petición con su token.</summary>
    private sealed class EmpresaDeLaCelda(Guid empresa) : IPlatformTenantAccessor
    {
        public PlatformTenantContext Current { get; } = new(empresa, IsSuperAdmin: false, IsService: true);
    }
}
