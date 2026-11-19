namespace Flit.Consultas.Api.Impuesto;

/// <summary>
/// Tipo de documento que entiende cada departamento. Un par que no está en la tabla no se consulta.
/// Diagnóstico manda <c>PAS</c>; el grupo común lo escribe <c>PASAP</c>.
/// </summary>
internal static class HomologacionDocumentoImpuesto
{
    public const string Valle = "76";

    private static readonly HashSet<string> Comunes =
    [
        "05", "25", "13", "68", "08", "11", "81", "18", "41", "44", "17", "94", "50", "52", "63",
    ];

    public static bool Conocido(string codigo) => codigo == Valle || Comunes.Contains(codigo);

    public static string? Homologar(string departamento, string? tipo)
    {
        var codigo = tipo?.Trim().ToUpperInvariant() ?? "";
        if (departamento == Valle)
        {
            return codigo switch
            {
                "CC" => "Cédula Ciudadanía",
                "NIT" => "NIT",
                "CE" => "Cédula Extranjería",
                "TI" => "Tarjeta de Identidad",
                "PAS" => "Pasaporte",
                "CD" => "Carnet Diplomático",
                _ => null,
            };
        }

        if (!Comunes.Contains(departamento))
            return null;
        return codigo switch
        {
            "CC" => "CC",
            "NIT" => "NIT",
            "CE" => "CE",
            "TI" => "TI",
            "PAS" => "PASAP",
            "RC" => "RC",
            _ => null,
        };
    }
}
