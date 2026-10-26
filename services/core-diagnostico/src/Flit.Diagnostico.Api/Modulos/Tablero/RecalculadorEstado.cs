using Flit.Diagnostico.Api.Modulos.Consultas;
using Flit.Diagnostico.Api.Modulos.Diagnosticos;
using Flit.Diagnostico.Api.Modulos.Trabajos;
using Flit.Diagnostico.Api.Persistence;
using Flit.Diagnostico.Domain.Consultas;
using Flit.Diagnostico.Domain.Diagnosticos;
using Flit.Platform.Sdk.Messaging;
using Microsoft.EntityFrameworkCore;

namespace Flit.Diagnostico.Api.Modulos.Tablero;

/// <summary>
/// Recalcula el estado global del lote tras cada celda terminal (HU #13743, F04-C4, plan §3.5): el procesador suma
/// <see cref="TrasCerrar"/> a los efectos del cierre de la celda, así que corre en la MISMA transacción que la cierra (después
/// de que <see cref="OrquestadorVehiculo"/> libere o bloquee las demás celdas del vehículo) y con el contexto de la empresa de
/// la celda. Si con eso todas las celdas aplicables del lote quedan en COMPLETADO o NA, el lote pasa a COMPLETADO con
/// <c>cerrado_en</c> y una fila del historial AUTO sin usuario (<see cref="EstadoGlobalLote"/>); con alguna en ERROR sigue
/// EN_PROCESO hasta el cierre del panel. Un reintento (la celda vuelve a PENDIENTE) no recalcula. Como el escritor de celdas
/// ya subió la versión del tablero con un UPDATE de la cabecera, la fila está bloqueada hasta el commit: dos celdas del mismo
/// lote que cierran a la vez se recalculan una detrás de otra y la transición ocurre una sola vez. Cuando esa transición deja el
/// lote en COMPLETADO o INCOMPLETO, la misma transacción encola el evento <see cref="LoteTerminado"/> (HU #13743, ADR-0075) en la
/// outbox del contexto de la celda; un lote reabierto (reproceso) que se vuelve a cerrar lo emite otra vez con su nueva versión.
/// </summary>
internal static class RecalculadorEstado
{
    /// <summary>
    /// El resultado con el recálculo sumado al final de sus efectos; sin cambios si la celda no queda terminal. <paramref name="outbox"/>
    /// es la del mismo contexto con que se cierra la celda (la del scope del procesador).
    /// </summary>
    public static ResultadoCelda TrasCerrar(CeldaTomada celda, ResultadoCelda resultado, TimeProvider reloj, IPlatformOutbox outbox)
    {
        ArgumentNullException.ThrowIfNull(celda);
        ArgumentNullException.ThrowIfNull(resultado);
        ArgumentNullException.ThrowIfNull(reloj);
        ArgumentNullException.ThrowIfNull(outbox);
        if (resultado.Estado is not (EstadoCelda.Completado or EstadoCelda.Error or EstadoCelda.Na) || resultado.ReintentarDesde is not null)
            return resultado;

        var propios = resultado.Efectos;
        return resultado with
        {
            Efectos = async (db, version, ct) =>
            {
                if (propios is not null)
                    await propios(db, version, ct).ConfigureAwait(false);
                var ahora = reloj.GetUtcNow();
                var transicion = await RecalcularAsync(db, celda.DiagnosticoId, ahora, ct).ConfigureAwait(false);
                // HU #13743 (ADR-0075, Q8): el lote terminado sale en la transacción de su cierre.
                if (transicion?.Nuevo is EstadoDiagnostico.Completado or EstadoDiagnostico.Incompleto)
                    await LoteTerminado.EncolarAsync(db, outbox, celda.DiagnosticoId, version, ahora, ct).ConfigureAwait(false);
            },
        };
    }

    /// <summary>
    /// Aplica la transición del lote si sus celdas la piden (sin guardar: lo hace quien cierra la celda) y la devuelve; null si
    /// el estado no cambia. Las celdas seguidas por el contexto cuentan con su valor en memoria (la que se está cerrando y las
    /// que el orquestador acaba de liberar o bloquear).
    /// </summary>
    public static Task<TransicionLote?> RecalcularAsync(DiagnosticoDb db, Guid diagnosticoId, DateTimeOffset ahora, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        return RecalcularAsync(db, db.Set<CabeceraDiagnostico>(), db.Set<CeldaConsulta>(), diagnosticoId, ahora, ct);
    }

    /// <summary>
    /// El mismo recálculo leyendo el lote y sus celdas de <paramref name="cabeceras"/> y <paramref name="celdas"/> (con
    /// seguimiento de cambios en la cabecera): el llenado manual del backoffice (HU #13800) pasa las filas de la empresa
    /// explícita (<c>EscrituraEntreEmpresas</c>), porque su contexto es el de la compañía FLIT.
    /// </summary>
    internal static async Task<TransicionLote?> RecalcularAsync(
        DiagnosticoDb db, IQueryable<CabeceraDiagnostico> cabeceras, IQueryable<CeldaConsulta> celdas, Guid diagnosticoId, DateTimeOffset ahora, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(cabeceras);
        ArgumentNullException.ThrowIfNull(celdas);
        var estadoActual = await cabeceras.AsNoTracking()
            .Where(c => c.Id == diagnosticoId).Select(c => c.Estado).SingleAsync(ct).ConfigureAwait(false);
        if (estadoActual != EstadoDiagnostico.EnProceso)
            return null;

        var enMemoria = db.ChangeTracker.Entries<CeldaConsulta>()
            .Where(e => e.Entity.DiagnosticoId == diagnosticoId)
            .ToDictionary(e => e.Entity.Id, e => e.Entity.Estado);
        var deBase = await celdas.AsNoTracking()
            .Where(c => c.DiagnosticoId == diagnosticoId)
            .Select(c => new { c.Id, c.Estado })
            .ToListAsync(ct).ConfigureAwait(false);
        var estados = deBase.Select(c => enMemoria.TryGetValue(c.Id, out var estado) ? estado : c.Estado);

        var nuevo = EstadoGlobalLote.Calcular(estadoActual, estados);
        if (nuevo == estadoActual)
            return null;

        var transicion = TransicionesLote.Transicionar(estadoActual, nuevo, DisparadorEstado.Auto);
        var cabecera = await cabeceras.SingleAsync(c => c.Id == diagnosticoId, ct).ConfigureAwait(false);
        cabecera.Estado = transicion.Nuevo;
        cabecera.CerradoEn = ahora;
        db.Add(new HistorialEstadoDiagnostico
        {
            TenantId = cabecera.TenantId,
            DiagnosticoId = cabecera.Id,
            EstadoAnterior = transicion.Anterior,
            EstadoNuevo = transicion.Nuevo,
            Disparador = transicion.Disparador,
            En = ahora,
        });
        return transicion;
    }
}
