using Flit.Diagnostico.Api.Modulos.Consultas;
using Flit.Diagnostico.Api.Modulos.Diagnosticos;
using Flit.Diagnostico.Api.Modulos.Vehiculos;
using Flit.Diagnostico.Api.Persistence;
using Flit.Diagnostico.Domain.Consultas;
using Flit.Diagnostico.Domain.Diagnosticos;
using Flit.Platform.Sdk.Messaging;
using Microsoft.EntityFrameworkCore;

namespace Flit.Diagnostico.Api.Modulos.Tablero;

/// <summary>
/// Recalcula el estado global del lote tras cada celda terminal (HU #13743, F04-C4, plan §3.5): el cierre de la celda
/// (<see cref="Consultas.Bus.PublicadorCeldas.CerrarAsync"/>, ADR-0075) lo llama al final, así que corre en la MISMA transacción que la cierra (después
/// de que <see cref="OrquestadorVehiculo"/> libere o bloquee las demás celdas del vehículo) y con el contexto de la empresa de
/// la celda. Si con eso todas las celdas aplicables del lote quedan en COMPLETADO o NA, el lote pasa a COMPLETADO con
/// <c>cerrado_en</c> y una fila del historial AUTO sin usuario (<see cref="EstadoGlobalLote"/>); con alguna en ERROR sigue
/// EN_PROCESO hasta el cierre del panel. Un reintento (la celda vuelve a PENDIENTE) no recalcula. Como el escritor de celdas
/// ya subió la versión del tablero con un UPDATE de la cabecera, la fila está bloqueada hasta el commit: dos celdas del mismo
/// lote que cierran a la vez se recalculan una detrás de otra y la transición ocurre una sola vez. Cuando una transición deja el
/// lote en COMPLETADO o INCOMPLETO (aquí, por el llenado manual o por el cierre del panel: todos pasan por
/// <see cref="AplicarTransicionAsync"/>), la misma transacción encola el evento <see cref="LoteTerminado"/> (HU #13743, ADR-0075);
/// un lote reabierto (reproceso, llenado manual) que se vuelve a cerrar lo emite otra vez con su nueva versión.
/// </summary>
internal static class RecalculadorEstado
{
    /// <summary>
    /// Aplica la transición del lote si sus celdas la piden (sin guardar: lo hace quien cierra la celda) y la devuelve; null si
    /// el estado no cambia. Las celdas seguidas por el contexto cuentan con su valor en memoria (la que se está cerrando y las
    /// que el orquestador acaba de liberar o bloquear). Si el lote queda terminado, encola <see cref="LoteTerminado"/> con
    /// <paramref name="versionTablero"/> en <paramref name="outbox"/> (la del mismo contexto).
    /// </summary>
    public static Task<TransicionLote?> RecalcularAsync(
        DiagnosticoDb db, IPlatformOutbox outbox, Guid diagnosticoId, long versionTablero, DateTimeOffset ahora, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        return RecalcularAsync(db, outbox, db.Set<CabeceraDiagnostico>(), db.Set<CeldaConsulta>(), db.Set<VehiculoDiagnostico>(), diagnosticoId, versionTablero, ahora, ct);
    }

    /// <summary>
    /// El mismo recálculo leyendo el lote, sus celdas y sus vehículos de <paramref name="cabeceras"/>, <paramref name="celdas"/> y
    /// <paramref name="vehiculos"/> (con seguimiento de cambios en la cabecera): el llenado manual del backoffice (HU #13800) pasa
    /// las filas de la empresa explícita (<c>EscrituraEntreEmpresas</c>), porque su contexto es el de la compañía FLIT.
    /// </summary>
    internal static async Task<TransicionLote?> RecalcularAsync(
        DiagnosticoDb db, IPlatformOutbox outbox, IQueryable<CabeceraDiagnostico> cabeceras, IQueryable<CeldaConsulta> celdas,
        IQueryable<VehiculoDiagnostico> vehiculos, Guid diagnosticoId, long versionTablero, DateTimeOffset ahora, CancellationToken ct)
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
        await AplicarTransicionAsync(db, outbox, cabecera, transicion, ahora, null, celdas, vehiculos, versionTablero, ct).ConfigureAwait(false);
        return transicion;
    }

    /// <summary>
    /// Punto común de los tres caminos que cambian el estado del lote al cerrar (el automático de <see cref="TrasCerrar"/>, el
    /// llenado manual y el cierre del panel, HU #13743): fija el estado y <c>cerrado_en</c>, agrega el historial y, si el lote queda
    /// en COMPLETADO o INCOMPLETO, encola <see cref="LoteTerminado"/> en la outbox del mismo contexto, una sola vez por transición
    /// (ADR-0075, Q8). No guarda: lo hace quien abrió la transacción.
    /// </summary>
    internal static async Task AplicarTransicionAsync(
        DiagnosticoDb db, IPlatformOutbox outbox, CabeceraDiagnostico cabecera, TransicionLote transicion, DateTimeOffset ahora, Guid? usuarioId,
        IQueryable<CeldaConsulta> celdas, IQueryable<VehiculoDiagnostico> vehiculos, long versionTablero, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(outbox);
        ArgumentNullException.ThrowIfNull(cabecera);
        ArgumentNullException.ThrowIfNull(transicion);
        cabecera.Estado = transicion.Nuevo;
        cabecera.CerradoEn = ahora;
        db.Add(new HistorialEstadoDiagnostico
        {
            TenantId = cabecera.TenantId,
            DiagnosticoId = cabecera.Id,
            EstadoAnterior = transicion.Anterior,
            EstadoNuevo = transicion.Nuevo,
            Disparador = transicion.Disparador,
            UsuarioId = usuarioId,
            En = ahora,
        });
        if (transicion.Nuevo is EstadoDiagnostico.Completado or EstadoDiagnostico.Incompleto)
            await LoteTerminado.EncolarAsync(db, outbox, cabecera, celdas, vehiculos, versionTablero, ahora, ct).ConfigureAwait(false);
    }
}
