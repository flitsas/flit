namespace Flit.Comparendos.Domain.Identificadores;

/// <summary>
/// NIT monitoreado. Las fuentes pueden tener el comparendo con o sin dígito de verificación, así que se consultan las
/// dos variantes (ADO #12956 «Multi-NIT y variantes»). Se guarda la base sin puntos ni guiones.
/// </summary>
public static class Nit
{
    private static readonly int[] Pesos = [3, 7, 13, 17, 19, 23, 29, 37, 41, 43, 47, 53, 59, 67, 71];

    /// <summary>
    /// Separa base y dígito de verificación de lo que digitó el usuario: «900.123.456-7» → (900123456, 7);
    /// «900123456» → (900123456, null). Devuelve null si no es un NIT (vacío, letras o más de 15 dígitos).
    /// </summary>
    public static (string Base, int? Dv)? Separar(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto))
            return null;

        var partes = texto.Trim().Split('-', StringSplitOptions.TrimEntries);
        if (partes.Length > 2)
            return null;

        var baseNit = new string(partes[0].Where(ch => ch is not ('.' or ' ' or ',')).ToArray());
        if (baseNit.Length is 0 or > 15 || !baseNit.All(char.IsAsciiDigit))
            return null;

        baseNit = baseNit.TrimStart('0');
        if (baseNit.Length == 0)
            return null;

        if (partes.Length == 1)
            return (baseNit, null);

        return partes[1].Length == 1 && char.IsAsciiDigit(partes[1][0]) ? (baseNit, partes[1][0] - '0') : null;
    }

    /// <summary>Dígito de verificación de la DIAN (módulo 11 con los pesos oficiales).</summary>
    public static int DigitoVerificacion(string baseNit)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseNit);
        if (baseNit.Length > Pesos.Length || !baseNit.All(char.IsAsciiDigit))
            throw new ArgumentException("El NIT solo admite hasta 15 dígitos.", nameof(baseNit));

        var suma = 0;
        for (var i = 0; i < baseNit.Length; i++)
            suma += (baseNit[baseNit.Length - 1 - i] - '0') * Pesos[i];

        var residuo = suma % 11;
        return residuo is 0 or 1 ? residuo : 11 - residuo;
    }

    /// <summary>Las dos formas en que se consulta a las fuentes: sin y con dígito de verificación.</summary>
    public static IReadOnlyList<string> Variantes(string baseNit) =>
        [baseNit, string.Concat(baseNit, DigitoVerificacion(baseNit).ToString(System.Globalization.CultureInfo.InvariantCulture))];

    /// <summary>
    /// Un NIT como lo trae una fuente, para mostrarlo «base-DV». Si viene con guion, se respeta. Si viene pegado al dígito
    /// («8110117798») y coincide con uno de los NITs conocidos (el vigilado o el de la empresa) más su dígito, es ese NIT.
    /// Si no, se toma como base y se le calcula el dígito (hallazgos B6-2 y B9-5).
    /// </summary>
    public static string? ConDigito(string? documento, params string?[] basesConocidas)
    {
        if (Separar(documento) is not { } s)
            return documento;
        if (s.Dv is { } dv)
            return $"{s.Base}-{dv}";
        var conocida = basesConocidas.FirstOrDefault(b => !string.IsNullOrEmpty(b) && s.Base == string.Concat(b, DigitoVerificacion(b!).ToString(System.Globalization.CultureInfo.InvariantCulture)));
        var baseNit = conocida ?? s.Base;
        return $"{baseNit}-{DigitoVerificacion(baseNit)}";
    }
}
