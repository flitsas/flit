using Flit.Diagnostico.Domain.Dictamen;

namespace Flit.Diagnostico.Tests.Dictamen;

/// <summary>
/// Una fila de la matriz por clase de vehículo (ola 2026-11-11, hallazgos V4/V5 y punto 7). <see cref="ClaseRunt"/> y
/// <see cref="ServicioRunt"/> son textos como los entrega el RUNT; <see cref="AniosRtm"/> es el plazo de la primera
/// revisión técnico-mecánica desde la matrícula (null = la RTM no aplica). <see cref="CodigoSoat"/> es la regla que
/// dispara un SOAT no vigente (null = el SOAT no aplica) y <see cref="EfectoSoat"/> su efecto fuera del unilateral.
/// </summary>
public sealed record FilaMatrizClase(
    string Nombre,
    string? ClaseRunt,
    string? ServicioRunt,
    ClaseVehiculoDictamen Clase,
    int? AniosRtm,
    string? CodigoSoat,
    string? EfectoSoat);

/// <summary>
/// Matriz por clase de vehículo del dictamen de expert-doc-engine (Ley 769 art. 52, mod. Decreto 019/2012 art. 202):
/// RTM a los 2 años para servicio público, moto y motocarro; a los 5 para el carro particular; maquinaria y remolque
/// sin RTM. SOAT: el remolque está exento; la maquinaria advierte sin bloquear. La consumen
/// <see cref="MatrizClaseVehiculoDictamenTests"/> (Bug RTM/SOAT) y la matriz del punto 7 (placas especiales).
/// </summary>
/// <example>
/// <code>
/// foreach (var fila in MatrizClaseVehiculo.Filas) { /* fila.ClaseRunt, fila.AniosRtm, fila.CodigoSoat… */ }
/// </code>
/// </example>
public static class MatrizClaseVehiculo
{
    /// <summary>Antigüedades de matrícula que recorre la matriz, en años.</summary>
    public static readonly int[] Antiguedades = [1, 2, 4, 5, 6];

    /// <summary>Las seis clases de la matriz del punto 7. Valores inventados con la forma del RUNT.</summary>
    public static readonly FilaMatrizClase[] Filas =
    [
        new("carro particular", "AUTOMOVIL", "Particular", ClaseVehiculoDictamen.Carro, 5, "SOAT_NO_VIGENTE", "BLOQUEA"),
        new("carro de servicio público", "CAMIONETA", "Público", ClaseVehiculoDictamen.Carro, 2, "SOAT_NO_VIGENTE", "BLOQUEA"),
        new("moto", "MOTOCICLETA", "Particular", ClaseVehiculoDictamen.Moto, 2, "SOAT_NO_VIGENTE", "BLOQUEA"),
        new("motocarro", "MOTOCARRO", "Particular", ClaseVehiculoDictamen.Motocarro, 2, "SOAT_NO_VIGENTE", "BLOQUEA"),
        new("maquinaria", "MAQUINARIA AGRÍCOLA", "Particular", ClaseVehiculoDictamen.Maquinaria, null, "SOAT_MAQUINARIA", "ADVIERTE"),
        new("remolque", "SEMIRREMOLQUE", "Particular", ClaseVehiculoDictamen.Remolque, null, null, null),
    ];

    /// <summary>La fila por nombre.</summary>
    public static FilaMatrizClase Fila(string nombre) => Filas.Single(f => f.Nombre == nombre);
}
