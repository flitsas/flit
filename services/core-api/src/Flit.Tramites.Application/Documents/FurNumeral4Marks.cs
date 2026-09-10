namespace Flit.Tramites.Application.Documents;

/// <summary>
/// Casillas del numeral 4 del FUR. El id es <c>vehicle_class_{field_to_fill}</c> normalizado
/// (catálogo <c>tramites.vehicle_classification_fur.field_to_fill</c>).
/// </summary>
public static class FurNumeral4Marks
{
    public const string Prefix = "vehicle_class_";

    public static readonly string[] Automotor =
    [
        "vehicle_class_AUTOMOVIL",
        "vehicle_class_BUS",
        "vehicle_class_BUSETA",
        "vehicle_class_CAMION",
        "vehicle_class_CAMIONETA",
        "vehicle_class_CAMPERO",
        "vehicle_class_MICROBUS",
        "vehicle_class_TRACTOCAMION",
        "vehicle_class_MOTOCICLETA",
        "vehicle_class_MOTOCARRO",
        "vehicle_class_MOTOTRICICLO",
        "vehicle_class_CUATRIMOTO",
        "vehicle_class_VOLQUETA",
        "vehicle_class_OTRO",
    ];

    public static readonly string[] Maquinaria =
    [
        "vehicle_class_AGRICOLA",
        "vehicle_class_INDUSTRIAL",
        "vehicle_class_CONSTRUCCION",
        "vehicle_class_OTROS",
    ];

    public static readonly string[] Remolques =
    [
        "vehicle_class_REMOLQUE",
        "vehicle_class_SEMIREMOLQUE",
        "vehicle_class_MULTIMODULAR",
        "vehicle_class_SIMILAR",
    ];

    public static string FieldId(string? fieldToFill)
    {
        var n = FurClassificationNormalizer.Normalize(fieldToFill);
        return n.Length == 0 ? string.Empty : Prefix + n;
    }

    public static IReadOnlyList<string> IdsFor(FurTemplateFormat format) => format switch
    {
        FurTemplateFormat.Maquinaria => Maquinaria,
        FurTemplateFormat.Remolques => Remolques,
        _ => Automotor,
    };
}
