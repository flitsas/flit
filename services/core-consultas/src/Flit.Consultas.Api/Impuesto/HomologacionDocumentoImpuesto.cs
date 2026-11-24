namespace Flit.Consultas.Api.Impuesto;

/// <summary>
/// Tipo de documento que entiende la fuente nueva (H10): una sola tabla para todos los departamentos.
/// Diagnóstico manda <c>PAS</c>; la fuente lo escribe <c>PA</c>. RC y nombres largos no se consultan.
/// </summary>
internal static class HomologacionDocumentoImpuesto
{
    /// <summary>
    /// Los 33 códigos DANE (32 departamentos y Bogotá D.C.). Protege solo la escritura del catálogo: es validación de forma, no de
    /// cobertura; la cobertura la decide la API de impuesto.
    /// </summary>
    private static readonly HashSet<string> DepartamentosConocidos =
    [
        "05", "08", "11", "13", "15", "17", "18", "19", "20", "23", "25", "27", "41", "44", "47", "50", "52",
        "54", "63", "66", "68", "70", "73", "76", "81", "85", "86", "88", "91", "94", "95", "97", "99",
    ];

    public static bool Conocido(string codigo) => DepartamentosConocidos.Contains(codigo);

    public static string? Homologar(string? tipo)
    {
        var codigo = tipo?.Trim().ToUpperInvariant() ?? "";
        return codigo switch
        {
            "CC" => "CC",
            "NIT" => "NIT",
            "CE" => "CE",
            "PAS" => "PA",
            "TI" => "TI",
            "CD" => "CD",
            _ => null,
        };
    }
}
