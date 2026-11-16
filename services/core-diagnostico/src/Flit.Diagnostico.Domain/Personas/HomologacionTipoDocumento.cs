using System.Text;

namespace Flit.Diagnostico.Domain.Personas;

/// <summary>
/// Homologación del tipo de documento a un código canónico (HU #14582, contrato §1.4, SP-3): la captura (CC, CE, NIT, PAS, TI) y
/// Intempo (C, N, «Cédula de ciudadanía»…) abrevian distinto, y la comparación del propietario declarado con el del RUNT exige el
/// mismo canónico. Sin distinguir mayúsculas, tildes, puntos ni espacios repetidos; los tipos que solo vienen del RUNT (RC, CD,
/// PPT, PEP) también se homologan, para que un propietario con ese tipo nunca «coincida» por error con otro. PPT nunca colapsa a
/// pasaporte (ADR-0082). Lo que no está en la tabla (también los códigos numéricos de Quipux) es null, nunca un canónico por
/// defecto. Regla pura: no ve números ni escribe logs.
/// </summary>
/// <example>
/// <code>
/// HomologacionTipoDocumento.Canonico("C")                    // "CC"
/// HomologacionTipoDocumento.Canonico("Cédula de extranjería") // "CE"
/// HomologacionTipoDocumento.Canonico("7")                    // null
/// </code>
/// </example>
public static class HomologacionTipoDocumento
{
    private static readonly Dictionary<string, string> Sinonimos = Tabla(
        ("CC", ["CC", "C", "CEDULA", "CEDULA DE CIUDADANIA", "CEDULA CIUDADANIA"]),
        ("CE", ["CE", "E", "CX", "CEDULA DE EXTRANJERIA", "CEDULA EXTRANJERIA"]),
        ("NIT", ["NIT", "N"]),
        ("TI", ["TI", "T", "TARJETA DE IDENTIDAD", "TARJETA IDENTIDAD"]),
        ("PAS", ["PAS", "PA", "PP", "P", "PASAPORTE"]),
        ("RC", ["RC", "R", "REGISTRO CIVIL"]),
        ("CD", ["CD", "D", "CARNE DIPLOMATICO"]),
        ("PPT", ["PPT", "PT"]),
        ("PEP", ["PEP"]));

    /// <summary>Código canónico del tipo (CC, CE, NIT, TI, PAS, RC, CD, PPT o PEP); null si está vacío o no está en la tabla.</summary>
    public static string? Canonico(string? tipo) =>
        tipo is null ? null : Sinonimos.GetValueOrDefault(Clave(tipo));

    /// <summary>Mayúsculas sin tildes ni puntos y con los espacios colapsados («C.C.» → «CC», «Cédula  de ciudadanía» → «CEDULA DE CIUDADANIA»).</summary>
    private static string Clave(string texto)
    {
        var limpio = new StringBuilder(texto.Length);
        foreach (var caracter in texto)
        {
            var c = char.ToUpperInvariant(caracter) switch
            {
                'Á' or 'À' or 'Ä' => 'A',
                'É' or 'È' or 'Ë' => 'E',
                'Í' or 'Ì' or 'Ï' => 'I',
                'Ó' or 'Ò' or 'Ö' => 'O',
                'Ú' or 'Ù' or 'Ü' => 'U',
                '\t' => ' ',
                var otro => otro,
            };
            if (c != '.')
                limpio.Append(c);
        }

        return string.Join(' ', limpio.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private static Dictionary<string, string> Tabla(params (string Canonico, string[] Aceptados)[] filas)
    {
        var tabla = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (canonico, aceptados) in filas)
        {
            foreach (var aceptado in aceptados)
                tabla.Add(aceptado, canonico);
        }

        return tabla;
    }
}
