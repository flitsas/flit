namespace Flit.DataMigration.V1.Mapping;

/// <summary>
/// Decodifica los campos que V1 guarda como CÓDIGO pero V2 guarda como TEXTO.
/// <para>
/// Detectado en la revisión: los traspasos migrados mostraban <c>vehicle_fuel = "1"</c> y
/// <c>vehicle_service = "1"</c>, mientras que los trámites nativos de V2 muestran <c>"GASOLINA"</c>
/// y <c>"Particular"</c>. Sin decodificar, en el frontend un trámite migrado se vería con números
/// donde uno nativo muestra texto.
/// </para>
/// </summary>
public static class VehicleCodeMap
{
    /// <summary>
    /// Combustible. Fuente autoritativa: catálogo <c>fuel_type</c> de V1 (code → description).
    /// Las descripciones coinciden con el vocabulario que usa V2 en sus trámites nativos.
    /// </summary>
    private static readonly Dictionary<string, string> Fuel = new(StringComparer.Ordinal)
    {
        ["1"] = "GASOLINA",
        ["2"] = "GNV",
        ["3"] = "DIESEL",
        ["4"] = "GAS GASOL",
        ["5"] = "ELECTRICO",
        ["6"] = "HIDROGENO",
        ["7"] = "ETANOL",
        ["8"] = "BIODIESEL",
        ["9"] = "GLP",
        ["10"] = "GASO ELEC",
        ["11"] = "DIES ELEC",
        ["12"] = "DIESEL GAS",
    };

    /// <summary>
    /// Tipo de servicio. V1 no tiene catálogo; se infiere de los datos y coincide con V2 nativo
    /// (<c>Particular</c> / <c>Público</c>). El código <c>0</c> (y cualquier otro) NO se adivina:
    /// se preserva el valor crudo y se avisa.
    /// </summary>
    private static readonly Dictionary<string, string> Service = new(StringComparer.Ordinal)
    {
        ["1"] = "Particular",
        ["2"] = "Público",
    };

    public static string DecodeFuel(string code, out bool unknown) => Decode(Fuel, code, out unknown);

    public static string DecodeService(string code, out bool unknown) => Decode(Service, code, out unknown);

    /// <summary>
    /// Traduce el código a texto. Si el código no está en el catálogo, devuelve el valor crudo
    /// y marca <paramref name="unknown"/> — nunca inventa una descripción.
    /// </summary>
    private static string Decode(Dictionary<string, string> map, string code, out bool unknown)
    {
        if (map.TryGetValue(code.Trim(), out var text))
        {
            unknown = false;
            return text;
        }

        unknown = true;
        return code;
    }
}
