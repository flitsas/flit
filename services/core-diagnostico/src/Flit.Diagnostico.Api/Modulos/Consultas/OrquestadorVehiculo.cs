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
/// empresa); las reglas N/A que se evalúan antes de liberar son de F03-H5.
/// </summary>
/// <example>
/// <code>
/// resultado = OrquestadorVehiculo.TrasEjecutar(celdaTomada, resultado, reloj);
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
    public static ResultadoCelda TrasEjecutar(Trabajos.CeldaTomada celda, ResultadoCelda resultado, TimeProvider reloj)
    {
        ArgumentNullException.ThrowIfNull(celda);
        ArgumentNullException.ThrowIfNull(resultado);
        ArgumentNullException.ThrowIfNull(reloj);
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
                    await LiberarCeldasDelVehiculoAsync(db, celda.VehiculoId, reloj.GetUtcNow(), version, ct).ConfigureAwait(false);
                else
                    await BloquearCeldasDelVehiculoAsync(db, celda.VehiculoId, reloj.GetUtcNow(), version, ct).ConfigureAwait(false);
            },
        };
    }

    /// <summary>
    /// Deja tomables ya (<c>disponible_desde = ahora</c>) las demás celdas del vehículo que esperaban a RUNT: las PENDIENTE
    /// diferidas y las ERROR con <see cref="BloqueadaPorRunt"/> (estas vuelven a PENDIENTE con <c>intentos_auto=0</c>, para el
    /// reproceso de F03-H8). No toca COMPLETADO, NA ni ERROR por otra causa. No guarda: lo hace quien cierra la celda.
    /// </summary>
    /// <returns>Cuántas celdas liberó.</returns>
    public static async Task<int> LiberarCeldasDelVehiculoAsync(DiagnosticoDb db, Guid vehiculoId, DateTimeOffset ahora, long versionTablero, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        var celdas = await db.Set<CeldaConsulta>()
            .Where(c => c.VehiculoId == vehiculoId && c.Fuente != Fuente.Runt
                && ((c.Estado == EstadoCelda.Pendiente && c.DisponibleDesde > ahora)
                    || (c.Estado == EstadoCelda.Error && c.TipoError == BloqueadaPorRunt)))
            .ToListAsync(ct).ConfigureAwait(false);
        foreach (var c in celdas)
        {
            c.Estado = EstadoCelda.Pendiente;
            c.TipoError = null;
            c.IntentosAuto = 0;
            c.DisponibleDesde = ahora;
            c.ActualizadoEn = ahora;
            c.VersionTablero = versionTablero;
        }

        return celdas.Count;
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
