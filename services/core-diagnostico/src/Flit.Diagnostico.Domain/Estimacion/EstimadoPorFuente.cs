using Flit.Diagnostico.Domain.Consultas;

namespace Flit.Diagnostico.Domain.Estimacion;

/// <summary>
/// Estimado de consultas externas de una fuente para un lote (HU #13747, F01-H3, R-11). Se expresa en número de consultas,
/// nunca en pesos: el precio lo fija el plan y la facturación sale de <c>ObtenerConsumo</c> de la plataforma.
/// </summary>
/// <param name="Fuente">Fuente del alcance del lote.</param>
/// <param name="Consultas">Consultas previstas: vehículos válidos menos los N/A previsibles de esa fuente.</param>
public sealed record EstimadoPorFuente(Fuente Fuente, int Consultas);
