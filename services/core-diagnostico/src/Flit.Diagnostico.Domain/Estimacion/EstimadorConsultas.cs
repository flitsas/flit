using Flit.Diagnostico.Domain.Consultas;

namespace Flit.Diagnostico.Domain.Estimacion;

/// <summary>
/// Estimado de consultas por fuente de un lote (HU #13747, F01-H3, R-11): por cada fuente del alcance, los vehículos válidos
/// menos los N/A previsibles. En D1 los N/A previsibles son <see cref="Fuente.RuntLocatario"/> y
/// <see cref="Fuente.SimitLocatario"/> de los vehículos sin documento de locatario declarado. Servicio puro: no toca base ni
/// HTTP, y la visibilidad la decide quien llama según <c>estimado_consultas</c> de la configuración (#13716).
/// </summary>
/// <example>
/// <code>
/// EstimadorConsultas.Estimar([Fuente.Runt, Fuente.RuntLocatario], vehiculos) // 10 válidos, 4 sin locatario
/// // → [EstimadoPorFuente(Runt, 10), EstimadoPorFuente(RuntLocatario, 6)]
/// </code>
/// </example>
public static class EstimadorConsultas
{
    /// <summary>
    /// Calcula el estimado por fuente, en el orden del alcance y sin repetir fuentes. Un lote vacío da 0 por fuente.
    /// </summary>
    /// <param name="alcance">Fuentes elegidas para el lote.</param>
    /// <param name="vehiculos">Vehículos capturados; los inválidos no cuentan.</param>
    public static IReadOnlyList<EstimadoPorFuente> Estimar(IEnumerable<Fuente> alcance, IEnumerable<VehiculoAEstimar> vehiculos)
    {
        ArgumentNullException.ThrowIfNull(alcance);
        ArgumentNullException.ThrowIfNull(vehiculos);

        var validos = 0;
        var conLocatario = 0;
        foreach (var vehiculo in vehiculos)
        {
            if (!vehiculo.EsValido)
            {
                continue;
            }

            validos++;
            if (vehiculo.TieneDocumentoLocatario)
            {
                conLocatario++;
            }
        }

        return alcance
            .Distinct()
            .Select(fuente => new EstimadoPorFuente(fuente, EsDeLocatario(fuente) ? conLocatario : validos))
            .ToList();
    }

    private static bool EsDeLocatario(Fuente fuente) => fuente is Fuente.RuntLocatario or Fuente.SimitLocatario;
}
