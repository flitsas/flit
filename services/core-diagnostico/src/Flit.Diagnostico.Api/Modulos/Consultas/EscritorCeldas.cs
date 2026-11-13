using Flit.Diagnostico.Api.Modulos.Diagnosticos;
using Flit.Diagnostico.Api.Modulos.Vehiculos;
using Flit.Diagnostico.Api.Persistence;
using Flit.Diagnostico.Domain.Consultas;
using Microsoft.EntityFrameworkCore;

namespace Flit.Diagnostico.Api.Modulos.Consultas;

/// <summary>
/// Escritura del cierre de una celda (HU #13727, base del sondeo de F04; desde ADR-0075 dentro de la transacción de quien cierra:
/// el consumidor de resultados, el publicador o el reconciliador). Sube <c>diagnosticos.version_tablero</c> y la copia en la
/// celda (<see cref="SubirVersionTableroAsync"/>) y deja la fila con su resultado (<see cref="Aplicar"/>). El contexto debe tener
/// la empresa de la celda: la escritura pasa por EF, así que <c>TenantFilter</c> filtra la lectura y valida el guardado, y la
/// subida de la versión (<c>ExecuteUpdate</c>) también lleva el filtro. La concurrencia la protege el <c>xmin</c> de la celda. El
/// cierre limpia el reproceso pendiente (HU #13746: ya quedó en el intento de esta ejecución) y la celda que reutiliza el dato del
/// mismo día (HU #13739, D-10) se cierra por este mismo camino, con <c>reutilizada_de_id</c>. No guarda.
/// </summary>
/// <example>
/// <code>
/// var version = await EscritorCeldas.SubirVersionTableroAsync(db, celda.DiagnosticoId, ct);
/// EscritorCeldas.Aplicar(celda, resultado, version, reloj.GetUtcNow());
/// </code>
/// </example>
internal static class EscritorCeldas
{
    /// <summary>Deja la celda COMPLETADO, ERROR o NA con su resultado y la versión del tablero de este cierre.</summary>
    public static void Aplicar(CeldaConsulta celda, ResultadoCelda resultado, long versionTablero, DateTimeOffset ahora)
    {
        ArgumentNullException.ThrowIfNull(celda);
        ArgumentNullException.ThrowIfNull(resultado);
        if (resultado.Estado is not (EstadoCelda.Completado or EstadoCelda.Error or EstadoCelda.Na))
            throw new ArgumentException("Una celda se cierra en COMPLETADO, ERROR o NA.", nameof(resultado));

        celda.Estado = resultado.Estado;
        celda.Payload = resultado.Payload;
        // HU #14087 AC8: la respuesta completa (cifrada) va con el resultado; sin respuesta de la fuente queda en null.
        celda.RespuestaFuenteCifrada = resultado.RespuestaFuenteCifrada;
        celda.MotivoNa = resultado.MotivoNa;
        celda.TipoError = resultado.Estado == EstadoCelda.Error ? resultado.TipoError : null;
        celda.Origen = resultado.Origen;
        celda.DatoObtenidoEn = resultado.DatoObtenidoEn;
        // HU #13739 (D-10): la celda que copió el dato del mismo día apunta a la original.
        celda.ReutilizadaDeId = resultado.Estado == EstadoCelda.Completado ? resultado.ReutilizadaDeId : null;
        celda.LeaseHasta = null;
        // HU #13746: el reproceso se consumió en esta ejecución.
        celda.ReprocesoUsuarioId = null;
        celda.ActualizadoEn = ahora;
        celda.VersionTablero = versionTablero;
    }

    /// <summary>
    /// Sube la versión del tablero del lote en <paramref name="contexto"/> (con la empresa de la celda) y la devuelve. El UPDATE
    /// bloquea la fila de la cabecera hasta el fin de la transacción, así que dos celdas del mismo lote nunca leen la misma versión.
    /// </summary>
    public static Task<long> SubirVersionTableroAsync(DiagnosticoDb contexto, Guid diagnosticoId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(contexto);
        return SubirVersionTableroAsync(contexto, contexto.Set<CabeceraDiagnostico>(), diagnosticoId, ct);
    }

    /// <summary>
    /// Lo mismo sobre <paramref name="cabeceras"/>: el backoffice (HU #14563 B7) pasa las de la empresa explícita
    /// (<c>EscrituraEntreEmpresas.ParaEditarDeLaEmpresa</c>), porque el filtro global de su contexto es el de FLIT.
    /// </summary>
    internal static async Task<long> SubirVersionTableroAsync(
        DiagnosticoDb contexto, IQueryable<CabeceraDiagnostico> cabeceras, Guid diagnosticoId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(contexto);
        ArgumentNullException.ThrowIfNull(cabeceras);
        var filas = await cabeceras.Where(c => c.Id == diagnosticoId)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.VersionTablero, c => c.VersionTablero + 1), ct).ConfigureAwait(false);
        if (filas != 1)
            throw new InvalidOperationException("La celda no tiene su cabecera en la empresa del contexto.");
        // ExecuteUpdate no refresca el xmin de la cabecera ya seguida. Si el cierre después le cambia el estado, el
        // SaveChanges chocaría con el token viejo.
        var seguida = contexto.ChangeTracker.Entries<CabeceraDiagnostico>().FirstOrDefault(e => e.Entity.Id == diagnosticoId);
        if (seguida is not null)
            await seguida.ReloadAsync(ct).ConfigureAwait(false);
        return seguida?.Entity.VersionTablero
            ?? await cabeceras.Where(c => c.Id == diagnosticoId).Select(c => c.VersionTablero).SingleAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Sube la versión de fila del tablero (HU #14563, ADR-0086 D-1) en la transacción del escritor: primero la cabecera
    /// (<see cref="SubirVersionTableroAsync"/>, que la bloquea hasta el commit) y después el vehículo con ese valor. Orden de
    /// bloqueo canónico: cabecera → vehículo → dictamen o ítem; llámala antes de apagar el dictamen vigente o de tocar el ítem.
    /// Si el vehículo está seguido, se asigna la propiedad y sale en el <c>SaveChanges</c> del escritor con su <c>xmin</c>; si
    /// no, va un <c>ExecuteUpdate</c>. Devuelve la versión nueva. No guarda.
    /// </summary>
    public static Task<long> SubirVersionFilaAsync(DiagnosticoDb contexto, Guid diagnosticoId, Guid vehiculoId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(contexto);
        return SubirVersionFilaAsync(contexto, contexto.Set<CabeceraDiagnostico>(), contexto.Set<VehiculoDiagnostico>(), diagnosticoId, vehiculoId, ct);
    }

    /// <summary>
    /// Lo mismo sobre <paramref name="cabeceras"/> y <paramref name="vehiculos"/> de UNA empresa: el ajuste FLIT del backoffice
    /// (HU #14563 B7) pasa las de la empresa explícita, con el mismo orden de bloqueo cabecera → vehículo → dictamen.
    /// </summary>
    internal static async Task<long> SubirVersionFilaAsync(
        DiagnosticoDb contexto, IQueryable<CabeceraDiagnostico> cabeceras, IQueryable<VehiculoDiagnostico> vehiculos,
        Guid diagnosticoId, Guid vehiculoId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(contexto);
        ArgumentNullException.ThrowIfNull(vehiculos);
        var version = await SubirVersionTableroAsync(contexto, cabeceras, diagnosticoId, ct).ConfigureAwait(false);
        var seguido = contexto.ChangeTracker.Entries<VehiculoDiagnostico>().FirstOrDefault(e => e.Entity.Id == vehiculoId);
        if (seguido is not null)
        {
            MarcarFila(seguido.Entity, version);
            return version;
        }

        var filas = await vehiculos.Where(v => v.Id == vehiculoId && v.DiagnosticoId == diagnosticoId)
            .ExecuteUpdateAsync(s => s.SetProperty(v => v.VersionTablero, version), ct).ConfigureAwait(false);
        if (filas != 1)
            throw new InvalidOperationException("El vehículo no pertenece al lote en la empresa del contexto.");
        return version;
    }

    /// <summary>
    /// Deja la fila con una versión ya obtenida en esta transacción (la de la celda que cierra y emite el dictamen, HU #14563 AC7):
    /// no sube la cabecera otra vez. La versión de fila es monótona.
    /// </summary>
    public static void MarcarFila(VehiculoDiagnostico vehiculo, long versionTablero)
    {
        ArgumentNullException.ThrowIfNull(vehiculo);
        if (versionTablero < vehiculo.VersionTablero)
            throw new InvalidOperationException("La versión de fila del tablero no baja.");
        vehiculo.VersionTablero = versionTablero;
    }
}
