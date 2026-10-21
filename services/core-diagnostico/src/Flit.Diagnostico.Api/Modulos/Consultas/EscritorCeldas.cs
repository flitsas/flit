using Flit.Diagnostico.Api.Modulos.Diagnosticos;
using Flit.Diagnostico.Api.Modulos.Trabajos;
using Flit.Diagnostico.Api.Persistence;
using Flit.Diagnostico.Domain.Consultas;
using Microsoft.EntityFrameworkCore;

namespace Flit.Diagnostico.Api.Modulos.Consultas;

/// <summary>
/// Escritor de celdas del procesador (HU #13727, ADR a, base del sondeo de F04): cierra una celda tomada con su resultado y,
/// en la MISMA transacción, sube <c>diagnosticos.version_tablero</c> y la copia en la celda. El contexto debe tener la empresa
/// de la celda (lo fija el procesador): la escritura pasa por EF, así que <c>TenantFilter</c> filtra la lectura y valida el
/// guardado, y la subida de la versión (<c>ExecuteUpdate</c>) también lleva el filtro. Solo cierra si la celda sigue siendo
/// de ESE lease (EN_CURSO con el mismo <c>lease_hasta</c>, y el <c>xmin</c> de la lectura): si venció y otro procesador la
/// retomó, no pisa su trabajo y devuelve false. Los <see cref="ResultadoCelda.Efectos"/> (datos del vehículo, liberación o
/// bloqueo de sus demás celdas, HU #13730) y el intento auditable (HU #13737) se aplican en esa misma transacción: o queda todo o
/// no queda nada. Un reintento (HU #13737) deja la celda en PENDIENTE con <c>disponible_desde</c> del backoff e
/// <c>intentos_auto</c> + 1. Una celda que reutiliza el dato del mismo día (HU #13739, D-10) se cierra por este mismo camino, con
/// <c>reutilizada_de_id</c>: sube la versión del tablero y dispara los mismos efectos que una llamada real.
/// </summary>
internal sealed class EscritorCeldas(DiagnosticoDb db, TimeProvider reloj)
{
    /// <summary>
    /// Cierra la celda (o la devuelve a la cola si <see cref="ResultadoCelda.ReintentarDesde"/> viene, HU #13737) e inserta el
    /// <paramref name="intento"/> en la misma transacción; false si el lease ya no es de quien la tomó (entonces no guarda nada).
    /// </summary>
    public async Task<bool> CerrarAsync(CeldaTomada tomada, ResultadoCelda resultado, CancellationToken ct, IntentoConsulta? intento = null)
    {
        ArgumentNullException.ThrowIfNull(tomada);
        ArgumentNullException.ThrowIfNull(resultado);
        if (db.EmpresaActual != tomada.TenantId)
            throw new InvalidOperationException("El escritor de celdas exige el contexto con la empresa de la celda.");
        var reintento = resultado.ReintentarDesde is not null;
        if (resultado.Estado == EstadoCelda.EnCurso || (resultado.Estado == EstadoCelda.Pendiente) != reintento)
            throw new ArgumentException("Una celda se cierra en COMPLETADO, ERROR o NA, o vuelve a PENDIENTE solo con ReintentarDesde.", nameof(resultado));

        // El reintento de Npgsql exige que la transacción del usuario vaya dentro de la estrategia de ejecución.
        return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var transaccion = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
            var celda = await db.Set<CeldaConsulta>().SingleOrDefaultAsync(c => c.Id == tomada.Id, ct).ConfigureAwait(false);
            if (celda is null || celda.Estado != EstadoCelda.EnCurso || celda.LeaseHasta != tomada.LeaseHasta)
                return false;

            var version = await SubirVersionAsync(celda.DiagnosticoId, ct).ConfigureAwait(false);
            celda.Estado = resultado.Estado;
            celda.Payload = resultado.Payload;
            celda.MotivoNa = resultado.MotivoNa;
            celda.TipoError = resultado.Estado == EstadoCelda.Error ? resultado.TipoError : null;
            celda.Origen = resultado.Origen;
            celda.DatoObtenidoEn = resultado.DatoObtenidoEn;
            // HU #13739 (D-10): la celda que copió el dato del mismo día apunta a la original.
            celda.ReutilizadaDeId = resultado.Estado == EstadoCelda.Completado ? resultado.ReutilizadaDeId : null;
            celda.LeaseHasta = null;
            celda.ActualizadoEn = reloj.GetUtcNow();
            celda.VersionTablero = version;
            if (reintento)
            {
                // Backoff de un transitorio (D-12): vuelve a la cola cuando venza la espera, con un reintento más.
                celda.DisponibleDesde = resultado.ReintentarDesde!.Value;
                celda.IntentosAuto++;
            }

            if (intento is not null)
                db.Add(intento);
            if (resultado.Efectos is { } efectos)
                await efectos(db, version, ct).ConfigureAwait(false);
            try
            {
                await db.SaveChangesAsync(ct).ConfigureAwait(false);
            }
            catch (DbUpdateConcurrencyException)
            {
                return false;
            }

            await transaccion.CommitAsync(ct).ConfigureAwait(false);
            return true;
        }).ConfigureAwait(false);
    }

    /// <summary>
    /// Sube la versión del tablero del lote y la devuelve. El UPDATE bloquea la fila de la cabecera hasta el fin de la
    /// transacción, así que dos celdas del mismo lote nunca leen la misma versión.
    /// </summary>
    private async Task<long> SubirVersionAsync(Guid diagnosticoId, CancellationToken ct)
    {
        var filas = await db.Set<CabeceraDiagnostico>().Where(c => c.Id == diagnosticoId)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.VersionTablero, c => c.VersionTablero + 1), ct).ConfigureAwait(false);
        if (filas != 1)
            throw new InvalidOperationException("La celda no tiene su cabecera en la empresa del contexto.");
        return await db.Set<CabeceraDiagnostico>().Where(c => c.Id == diagnosticoId).Select(c => c.VersionTablero).SingleAsync(ct).ConfigureAwait(false);
    }
}
