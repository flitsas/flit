namespace Flit.Consultas.Api.Impuesto;

/// <summary>
/// Tipo de documento que entiende la fuente nueva (H10): una sola tabla para todos los departamentos.
/// Diagnóstico manda <c>PAS</c>; la fuente lo escribe <c>PA</c>. RC y nombres largos no se consultan.
/// </summary>
internal static class HomologacionDocumentoImpuesto
{
    private static readonly HashSet<string> DepartamentosConocidos =
    [
        "05", "08", "11", "13", "17", "18", "25", "41", "44", "50", "52", "63", "68", "76", "81", "94",
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
