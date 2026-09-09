namespace Flit.Admin.Domain.GeneracionDocumental;

/// <summary>
/// Una de las once condiciones especiales de traspaso de los arts. 5.3.2.3 a 5.3.2.13
/// (anexo <c>docs/plantilla-transferencia-dominio.md</c> §4.0).
/// </summary>
/// <param name="Codigo">Identificador estable que viaja en el contrato HTTP y en <c>input_summary</c>.</param>
/// <param name="Articulo">Artículo de la Resolución 20233040017145 de 2023 que la regula.</param>
/// <param name="Titulo">Enunciado de la condición, tal como la nombra el anexo.</param>
public sealed record TransferSpecialCondition(string Codigo, string Articulo, string Titulo);

/// <summary>
/// Catálogo cerrado de las <b>once</b> condiciones especiales de traspaso de los arts. 5.3.2.3 a
/// 5.3.2.13 (anexo §4.0). Es la matriz de bloqueo de <c>VB-07</c>: declarar cualquiera de ellas
/// impide generar el documento, porque la operación no se rige por el art. 5.3.2.1 y exige soportes
/// o exenciones propios de su artículo que este instrumento no captura, no declara y no acredita.
///
/// <para><b>El art. 5.3.2.14 NO está aquí y no puede estarlo.</b> La expedición de la nueva licencia
/// de tránsito es el paso final común a TODO traspaso, no una condición especial: el anexo lo dice
/// expresamente en su nota de alcance. Meterlo en esta matriz bloquearía todos los traspasos del
/// módulo. El proyecto de pruebas congela ambas cosas:
/// que son once y que el 5.3.2.14 no es una de ellas.</para>
///
/// <para><b>El gate es declarativo.</b> FLIT no puede verificar por sí mismo si el vehículo es
/// blindado, si hay una sucesión en curso o si el PBV supera 10.500 kg; declara el usuario y la
/// falsedad la resuelve el Organismo de Tránsito al recibir el trámite (§4.0, nota de método).</para>
/// </summary>
public static class TransferSpecialRegime
{
    /// <summary>Vehículo de servicio público de pasajeros o mixto — art. 5.3.2.3.</summary>
    public const string ServicioPublicoPasajerosOMixto = "SERVICIO_PUBLICO_PASAJEROS_O_MIXTO";

    /// <summary>Traspaso a compañía de seguros por hurto — art. 5.3.2.4.</summary>
    public const string AseguradoraPorHurto = "ASEGURADORA_POR_HURTO";

    /// <summary>Traspaso a compañía de seguros por pérdida o destrucción parcial — art. 5.3.2.5.</summary>
    public const string AseguradoraPorPerdidaParcial = "ASEGURADORA_POR_PERDIDA_PARCIAL";

    /// <summary>Vehículo blindado — art. 5.3.2.6.</summary>
    public const string VehiculoBlindado = "VEHICULO_BLINDADO";

    /// <summary>Traspaso producto de decisión judicial o administrativa — art. 5.3.2.7.</summary>
    public const string DecisionJudicialOAdministrativa = "DECISION_JUDICIAL_O_ADMINISTRATIVA";

    /// <summary>Traspaso por sucesión — art. 5.3.2.8.</summary>
    public const string Sucesion = "SUCESION";

    /// <summary>Importación temporal por sustitución del importador — art. 5.3.2.9.</summary>
    public const string ImportacionTemporalSustitucionImportador = "IMPORTACION_TEMPORAL_SUSTITUCION_IMPORTADOR";

    /// <summary>Decomiso por la DIAN o adjudicación a favor de la Nación — art. 5.3.2.10.</summary>
    public const string DecomisoDianOAdjudicacionNacion = "DECOMISO_DIAN_O_ADJUDICACION_NACION";

    /// <summary>Comiso por la Fiscalía General de la Nación — art. 5.3.2.11.</summary>
    public const string ComisoFiscalia = "COMISO_FISCALIA";

    /// <summary>Vehículo enajenado por declaratoria de abandono — art. 5.3.2.12.</summary>
    public const string DeclaratoriaDeAbandono = "DECLARATORIA_DE_ABANDONO";

    /// <summary>Vehículo de carga con PBV superior a 10.500 kg — art. 5.3.2.13.</summary>
    public const string CargaPbvSuperior10500Kg = "CARGA_PBV_SUPERIOR_10500_KG";

    /// <summary>
    /// Las once condiciones, en el orden de la tabla del anexo §4.0. El orden es el de la norma y el
    /// que la interfaz enumera: no se reordena por comodidad de presentación.
    /// </summary>
    public static IReadOnlyList<TransferSpecialCondition> All { get; } =
    [
        new(ServicioPublicoPasajerosOMixto, "art. 5.3.2.3",
            "Vehículo de servicio público de pasajeros o mixto"),
        new(AseguradoraPorHurto, "art. 5.3.2.4",
            "Traspaso a compañía de seguros por hurto del vehículo"),
        new(AseguradoraPorPerdidaParcial, "art. 5.3.2.5",
            "Traspaso a compañía de seguros por pérdida o destrucción parcial"),
        new(VehiculoBlindado, "art. 5.3.2.6",
            "Vehículo blindado"),
        new(DecisionJudicialOAdministrativa, "art. 5.3.2.7",
            "Traspaso producto de decisión judicial o administrativa"),
        new(Sucesion, "art. 5.3.2.8",
            "Traspaso por sucesión"),
        new(ImportacionTemporalSustitucionImportador, "art. 5.3.2.9",
            "Importación temporal por sustitución del importador"),
        new(DecomisoDianOAdjudicacionNacion, "art. 5.3.2.10",
            "Decomiso por la DIAN o adjudicación a favor de la Nación"),
        new(ComisoFiscalia, "art. 5.3.2.11",
            "Comiso por la Fiscalía General de la Nación"),
        new(DeclaratoriaDeAbandono, "art. 5.3.2.12",
            "Vehículo enajenado por declaratoria de abandono"),
        new(CargaPbvSuperior10500Kg, "art. 5.3.2.13",
            "Vehículo de carga con PBV superior a 10.500 kg"),
    ];

    /// <summary>
    /// La condición del catálogo, o <c>null</c> si el código no es una de las once. Un código
    /// desconocido NO se convierte en condición especial por sí mismo: el art. 5.3.2.14, por
    /// ejemplo, cae aquí y devuelve <c>null</c> a propósito.
    /// </summary>
    public static TransferSpecialCondition? Find(string? codigo)
    {
        if (string.IsNullOrWhiteSpace(codigo))
        {
            return null;
        }

        var normalizado = codigo.Trim().ToUpperInvariant();
        return All.FirstOrDefault(c => string.Equals(c.Codigo, normalizado, StringComparison.Ordinal));
    }

    public static bool IsKnown(string? codigo) => Find(codigo) is not null;
}
