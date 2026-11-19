using Flit.Diagnostico.Domain.Consultas;

namespace Flit.Diagnostico.Domain.Estimacion;

/// <summary>
/// Estimado de consultas externas de una fuente para un lote (HU #13747, F01-H3, R-11). Se expresa en número de consultas,
/// nunca en pesos: el precio lo fija el plan y la facturación sale de <c>ObtenerConsumo</c> de la plataforma.
/// </summary>
/// <param name="Fuente">Fuente del alcance del lote.</param>
/// <param name="Consultas">Consultas previstas: vehículos válidos menos los N/A previsibles de esa fuente o, en las fuentes de
/// persona con las personas ya registradas, una por persona del rol (HU #13796).</param>
/// <param name="Minimo">Verdadero si <paramref name="Consultas"/> es un mínimo («al menos N», Q7): fuente de persona con algún
/// vehículo válido cuyas personas aún no se conocen (antes de lanzar, o sin filas en <c>personas_vehiculo</c>).</param>
public sealed record EstimadoPorFuente(Fuente Fuente, int Consultas, bool Minimo = false);
