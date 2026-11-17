namespace Flit.Diagnostico.Domain.Consultas;

/// <summary>Lo que el criterio «pegada» necesita de una celda: su vehículo, fuente, estado y último cambio.</summary>
public readonly record struct CeldaParaReintento(Guid Id, Guid VehiculoId, Fuente Fuente, EstadoCelda Estado, DateTimeOffset ActualizadoEn);

/// <summary>
/// Criterio «pegada» (ajuste 2026-11-11 Punto 3, decisión 3.2): una celda está pegada si está en ERROR, o en PENDIENTE/EN_CURSO
/// sin cambiar hace MÁS de <c>umbral</c> (por defecto <see cref="UmbralPorDefecto"/> = 10 min; <c>actualizado_en</c> es el último
/// cambio de la celda, V7). COMPLETADO y NA nunca. <see cref="Reencolables"/> es lo que reencola el reintento manual (por placa o
/// del lote) y lo que el tablero cuenta como placa pegada: respeta el orden de la RUNT (HU #13730) — si la RUNT del vehículo no está
/// COMPLETADO solo ella se reencola, porque las demás esperan su cierre (que las libera o las bloquea). Una celda recién reencolada
/// cambió hace menos del umbral y deja de contar: el reintento es idempotente (AC5). Puro: el instante lo da quien llama.
/// </summary>
/// <example>
/// <code>
/// var aReencolar = CriterioPegada.Reencolables(celdas, reloj.GetUtcNow(), opciones.UmbralPegada);
/// </code>
/// </example>
public static class CriterioPegada
{
    /// <summary>Tiempo sin cambio a partir del cual una celda PENDIENTE o EN_CURSO cuenta como pegada.</summary>
    public static readonly TimeSpan UmbralPorDefecto = TimeSpan.FromMinutes(10);

    /// <summary>Verdadero si la celda está en ERROR, o en PENDIENTE/EN_CURSO con su último cambio hace más de <paramref name="umbral"/>.</summary>
    public static bool EstaPegada(EstadoCelda estado, DateTimeOffset actualizadoEn, DateTimeOffset ahora, TimeSpan umbral) =>
        estado == EstadoCelda.Error
        || (estado is EstadoCelda.Pendiente or EstadoCelda.EnCurso && ahora - actualizadoEn > umbral);

    /// <summary>
    /// Las celdas pegadas que se reencolan, vehículo por vehículo: si la RUNT del vehículo está COMPLETADO (o no la tiene), todas
    /// sus pegadas; si no, solo la RUNT cuando ella misma está pegada.
    /// </summary>
    public static IReadOnlyList<CeldaParaReintento> Reencolables(IEnumerable<CeldaParaReintento> celdas, DateTimeOffset ahora, TimeSpan umbral)
    {
        ArgumentNullException.ThrowIfNull(celdas);
        var resultado = new List<CeldaParaReintento>();
        foreach (var vehiculo in celdas.GroupBy(c => c.VehiculoId))
        {
            var runt = vehiculo.Where(c => c.Fuente == Fuente.Runt).Select(c => (CeldaParaReintento?)c).FirstOrDefault();
            IEnumerable<CeldaParaReintento> candidatas = runt is { Estado: not EstadoCelda.Completado } r ? [r] : vehiculo;
            resultado.AddRange(candidatas.Where(c => EstaPegada(c.Estado, c.ActualizadoEn, ahora, umbral)));
        }

        return resultado;
    }
}
