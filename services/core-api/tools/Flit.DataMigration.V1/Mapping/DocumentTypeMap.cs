namespace Flit.DataMigration.V1.Mapping;

/// <summary>
/// Traduce el tipo de documento de V1 al vocabulario de V2.
/// <para>
/// V1 guarda la convención RUNT de UNA letra (<c>C</c>, <c>N</c>, <c>P</c>, <c>T</c>…),
/// mientras que V2 valida contra <c>{ CC, CE, NIT, PAS, TI }</c>
/// (ver <c>ActorsCommand</c>). Migrar el valor crudo dejaría actores con
/// <c>document_type = 'C'</c>, que la propia app de V2 rechazaría al editarlos.
/// </para>
/// </summary>
public static class DocumentTypeMap
{
    private static readonly Dictionary<string, string> Map = new(StringComparer.OrdinalIgnoreCase)
    {
        // Convención de una letra usada por los masters de V1.
        ["C"] = "CC",
        ["N"] = "NIT",
        ["E"] = "CE",
        ["P"] = "PAS",
        ["T"] = "TI",

        // Formas ya largas (algunas integraciones de V1 las escriben así).
        ["CC"] = "CC",
        ["NIT"] = "NIT",
        ["CE"] = "CE",
        ["PA"] = "PAS",
        ["PAS"] = "PAS",
        ["TI"] = "TI",
    };

    /// <summary>
    /// Tipo de documento válido en V2. Si el valor de V1 es desconocido se asume
    /// <c>CC</c> —el caso abrumadoramente mayoritario— y el llamador lo reporta como aviso:
    /// preferimos un dato utilizable + alerta visible que un actor que V2 no pueda editar.
    /// </summary>
    public static string ToV2(string? v1DocumentType, out bool wasUnknown)
    {
        if (!string.IsNullOrWhiteSpace(v1DocumentType) && Map.TryGetValue(v1DocumentType.Trim(), out var mapped))
        {
            wasUnknown = false;
            return mapped;
        }

        wasUnknown = true;
        return "CC";
    }
}
