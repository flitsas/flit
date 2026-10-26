using Flit.Diagnostico.Api.Modulos.Catalogos;
using Flit.Diagnostico.Api.Modulos.Consultas.Bus;
using Flit.Diagnostico.Api.Modulos.Vehiculos;
using Flit.Diagnostico.Api.Persistence;
using Flit.Diagnostico.Domain.Consultas;
using Microsoft.EntityFrameworkCore;

namespace Flit.Diagnostico.Api.Modulos.Consultas;

/// <summary>
/// Orden de las celdas de un vehículo (HU #13730, F03-C4/F03-C7, MOD03 paso 1): RUNT va primero y decide el resto. Al cerrar la
/// celda RUNT, en la MISMA transacción del cierre (<see cref="EfectosDeCierre"/>): si completó, se liberan las demás celdas del
/// vehículo (<see cref="LiberarCeldasDelVehiculoAsync"/>); si quedó en ERROR, pasan a ERROR con
/// <see cref="BloqueadaPorRunt"/> sin que se llame ninguna otra fuente (<see cref="BloquearCeldasDelVehiculoAsync"/>). Solo
/// toca las celdas de ESE vehículo: el resto del lote sigue. Escribe con el contexto de la celda (filtro y validación de su
/// empresa). Antes de liberar (HU #13733, F03-H5) deriva el departamento del organismo y aplica
/// <see cref="ReglasAplicabilidad"/>: las celdas que no aplican quedan en NA con su motivo, sin llamar a ninguna fuente ni
/// consumir. Solo RUNT bloquea al vehículo: el ERROR de otra celda (las de persona, R-24) no toca las demás. Cuando la RUNT
/// que completa es un reproceso (HU #13746), las bloqueadas que libera corren con el usuario de ese reproceso. Desde ADR-0075
/// (HU #13730) cada celda liberada publica además su trabajo de consulta (<see cref="PublicadorCeldas"/>) en esa misma
/// transacción, con lo que acaba de dejar la RUNT (placa, datos técnicos, personas y departamento aún sin guardar); las que
/// quedan en NA y las bloqueadas no publican.
/// </summary>
/// <example>
/// <code>
/// resultado = OrquestadorVehiculo.TrasEjecutar(celdaTomada, resultado, reloj, publicador);
/// await escritor.CerrarAsync(celdaTomada, resultado, ct); // cierra RUNT y libera o bloquea las demás, todo junto
/// </code>
/// </example>
internal static class OrquestadorVehiculo
{
    /// <summary><c>tipo_error</c> de una celda que no se consultó porque la RUNT de su vehículo quedó en ERROR.</summary>
    public const string BloqueadaPorRunt = "BLOQUEADA_POR_RUNT";

    /// <summary>
    /// El resultado con los efectos del orden por vehículo sumados a los del ejecutor; las celdas que no son RUNT, o una RUNT
    /// en N/A, pasan sin cambios.
    /// </summary>
    public static ResultadoCelda TrasEjecutar(Trabajos.CeldaTomada celda, ResultadoCelda resultado, TimeProvider reloj, PublicadorCeldas publicador)
    {
        ArgumentNullException.ThrowIfNull(celda);
        ArgumentNullException.ThrowIfNull(resultado);
        ArgumentNullException.ThrowIfNull(reloj);
        ArgumentNullException.ThrowIfNull(publicador);
        if (celda.Fuente != Fuente.Runt || resultado.Estado is not (EstadoCelda.Completado or EstadoCelda.Error))
            return resultado;

        var propios = resultado.Efectos;
        var completado = resultado.Estado == EstadoCelda.Completado;
        return resultado with
        {
            Efectos = async (db, version, ct) =>
            {
                if (propios is not null)
                    await propios(db, version, ct).ConfigureAwait(false);
                if (completado)
                    await LiberarCeldasDelVehiculoAsync(db, celda.VehiculoId, reloj.GetUtcNow(), version, publicador, ct, celda.ReprocesoUsuarioId).ConfigureAwait(false);
                else
                    await BloquearCeldasDelVehiculoAsync(db, celda.VehiculoId, reloj.GetUtcNow(), version, ct).ConfigureAwait(false);
            },
        };
    }

    /// <summary>
    /// Deja tomables ya (<c>disponible_desde = ahora</c>) las demás celdas del vehículo que esperaban a RUNT: las PENDIENTE
    /// diferidas y las ERROR con <see cref="BloqueadaPorRunt"/> (estas vuelven a PENDIENTE con <c>intentos_auto=0</c>, para el
    /// reproceso de F03-H8; si la RUNT que acaba de completar era un reproceso, heredan su <paramref name="reprocesoUsuarioId"/> y
    /// se ejecutan con <c>disparador=REPROCESO</c> y ese usuario, HU #13746). Antes deriva <c>vehiculos.departamento_codigo</c> del organismo (<see cref="DerivacionDepartamento"/>)
    /// y evalúa <see cref="ReglasAplicabilidad"/>: la celda que no aplica pasa a NA con su motivo en lugar de liberarse (HU
    /// #13733). No toca COMPLETADO, NA ni ERROR por otra causa. Cada celda liberada publica su trabajo con <paramref name="publicador"/>
    /// (ADR-0075, HU #13730), que debe ser el del mismo contexto <paramref name="db"/>. No guarda: lo hace quien cierra la celda.
    /// </summary>
    /// <returns>Cuántas celdas liberó (sin contar las que quedaron en NA).</returns>
    public static async Task<int> LiberarCeldasDelVehiculoAsync(
        DiagnosticoDb db, Guid vehiculoId, DateTimeOffset ahora, long versionTablero, PublicadorCeldas publicador, CancellationToken ct,
        Guid? reprocesoUsuarioId = null)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(publicador);
        var celdas = await db.Set<CeldaConsulta>()
            .Where(c => c.VehiculoId == vehiculoId && c.Fuente != Fuente.Runt
                && ((c.Estado == EstadoCelda.Pendiente && c.DisponibleDesde > ahora)
                    || (c.Estado == EstadoCelda.Error && c.TipoError == BloqueadaPorRunt)))
            .ToListAsync(ct).ConfigureAwait(false);
        var hechos = await HechosAsync(db, vehiculoId, ct).ConfigureAwait(false);
        var liberadas = 0;
        foreach (var c in celdas)
        {
            var motivo = ReglasAplicabilidad.Evaluar(c.Fuente, hechos);
            var bloqueada = c.Estado == EstadoCelda.Error;
            c.Estado = motivo is null ? EstadoCelda.Pendiente : EstadoCelda.Na;
            c.MotivoNa = motivo;
            c.TipoError = null;
            c.IntentosAuto = 0;
            c.DisponibleDesde = ahora;
            c.ActualizadoEn = ahora;
            c.VersionTablero = versionTablero;
            c.ReprocesoUsuarioId = motivo is null && bloqueada ? reprocesoUsuarioId : null;
            if (motivo is null)
            {
                liberadas++;
                // ADR-0075: el trabajo de la celda liberada sale en la transacción que cierra la RUNT.
                await publicador.PublicarAsync(c, ct).ConfigureAwait(false);
            }
        }

        return liberadas;
    }

    /// <summary>
    /// Los hechos del vehículo para las reglas de no aplica, con lo que acaba de dejar la RUNT (la entidad seguida por el
    /// contexto conserva los cambios del ejecutor aún sin guardar). Fija el departamento derivado en el vehículo.
    /// </summary>
    private static async Task<HechosAplicabilidad> HechosAsync(DiagnosticoDb db, Guid vehiculoId, CancellationToken ct)
    {
        var vehiculo = await db.Set<VehiculoDiagnostico>().SingleAsync(v => v.Id == vehiculoId, ct).ConfigureAwait(false);
        vehiculo.DepartamentoCodigo = await DerivacionDepartamento.DerivarAsync(db, vehiculo.OrganismoNombre, ct).ConfigureAwait(false);
        var soportado = await DerivacionDepartamento.SoportadoAsync(db, vehiculo.DepartamentoCodigo, ct).ConfigureAwait(false);
        return new HechosAplicabilidad(vehiculo.Servicio, vehiculo.DepartamentoCodigo, soportado, vehiculo.LocatarioDocOrigen);
    }

    /// <summary>
    /// Pasa a ERROR con <see cref="BloqueadaPorRunt"/> las demás celdas del vehículo que esperaban a RUNT (PENDIENTE); no se
    /// llama ninguna fuente. No toca las que ya tienen resultado. No guarda: lo hace quien cierra la celda.
    /// </summary>
    /// <returns>Cuántas celdas bloqueó.</returns>
    public static async Task<int> BloquearCeldasDelVehiculoAsync(DiagnosticoDb db, Guid vehiculoId, DateTimeOffset ahora, long versionTablero, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        var celdas = await db.Set<CeldaConsulta>()
            .Where(c => c.VehiculoId == vehiculoId && c.Fuente != Fuente.Runt && c.Estado == EstadoCelda.Pendiente)
            .ToListAsync(ct).ConfigureAwait(false);
        foreach (var c in celdas)
        {
            c.Estado = EstadoCelda.Error;
            c.TipoError = BloqueadaPorRunt;
            c.ActualizadoEn = ahora;
            c.VersionTablero = versionTablero;
        }

        return celdas.Count;
    }
}
