using Flit.Diagnostico.Domain.Consultas;

namespace Flit.Diagnostico.Domain.Estimacion;

/// <summary>
/// Estimado de consultas por fuente de un lote (HU #13747, F01-H3, R-11): por cada fuente del alcance, los vehículos válidos
/// menos los N/A previsibles. En D1 los N/A previsibles son <see cref="Fuente.RuntLocatario"/> y
/// <see cref="Fuente.SimitLocatario"/> de los vehículos sin documento de locatario declarado. Las fuentes de persona
/// (RUNT_/SIMIT_ PROPIETARIO y LOCATARIO, HU #13796) consultan una vez por persona: con las personas registradas del vehículo
/// cuentan una por persona del rol; sin ellas (antes de lanzar) cuentan un mínimo de uno por vehículo (locatario: uno por
/// vehículo con documento declarado) y la fuente queda marcada <see cref="EstimadoPorFuente.Minimo"/> (Q7, «al menos N»).
/// Servicio puro: no toca base ni HTTP, y la visibilidad la decide quien llama según <c>estimado_consultas</c> de la
/// configuración (#13716).
/// </summary>
/// <example>
/// <code>
/// EstimadorConsultas.Estimar([Fuente.Runt, Fuente.RuntLocatario], vehiculos) // 10 válidos, 4 sin locatario
/// // → [EstimadoPorFuente(Runt, 10), EstimadoPorFuente(RuntLocatario, 6, Minimo: true)]
/// EstimadorConsultas.Estimar([Fuente.RuntPropietario], [new(true, false, new PersonasRegistradas(3, 0))])
/// // → [EstimadoPorFuente(RuntPropietario, 3)]
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
        var propietarios = 0;
        var locatarios = 0;
        var sinPersonas = false;
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

            if (vehiculo.Personas is { } personas)
            {
                propietarios += personas.Propietarios;
                locatarios += personas.Locatarios;
            }
            else
            {
                sinPersonas = true;
                propietarios++;
                locatarios += vehiculo.TieneDocumentoLocatario ? 1 : 0;
            }
        }

        return alcance
            .Distinct()
            .Select(fuente => fuente switch
            {
                Fuente.RuntPropietario or Fuente.SimitPropietario => new EstimadoPorFuente(fuente, propietarios, sinPersonas),
                Fuente.RuntLocatario or Fuente.SimitLocatario => new EstimadoPorFuente(fuente, locatarios, sinPersonas),
                _ => new EstimadoPorFuente(fuente, validos),
            })
            .ToList();
    }
}
