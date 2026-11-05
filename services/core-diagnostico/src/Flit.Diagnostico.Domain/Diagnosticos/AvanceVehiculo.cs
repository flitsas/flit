using Flit.Diagnostico.Domain.Consultas;

namespace Flit.Diagnostico.Domain.Diagnosticos;

/// <summary>
/// Avance por vehículo (HU #13743, R-7): un vehículo está terminado cuando TODAS sus celdas están en COMPLETADO o NA, sin
/// esperar al resto del lote. Un vehículo sin celdas no está terminado. Una celda en ERROR no lo termina (se puede reprocesar).
/// </summary>
/// <example>
/// <code>
/// AvanceVehiculo.Terminado([EstadoCelda.Completado, EstadoCelda.Na]); // true
/// AvanceVehiculo.Terminado([EstadoCelda.Completado, EstadoCelda.Error]); // false
/// </code>
/// </example>
public static class AvanceVehiculo
{
    /// <summary>true si el vehículo tiene celdas y todas están en COMPLETADO o NA.</summary>
    public static bool Terminado(IEnumerable<EstadoCelda> celdas)
    {
        ArgumentNullException.ThrowIfNull(celdas);
        var alguna = false;
        foreach (var celda in celdas)
        {
            if (celda is not (EstadoCelda.Completado or EstadoCelda.Na))
                return false;
            alguna = true;
        }

        return alguna;
    }
}
