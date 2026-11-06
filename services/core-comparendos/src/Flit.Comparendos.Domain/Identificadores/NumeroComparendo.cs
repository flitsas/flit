namespace Flit.Comparendos.Domain.Identificadores;

/// <summary>
/// Llave de unicidad del comparendo por empresa (RN-021; GDC ID12 / HU #14367): sin espacios ni guiones.
/// Los prefijos alfabéticos iniciales («D-», «A-») se descartan y queda la secuencia numérica
/// («d-123 456» → «123456»).
/// </summary>
public static class NumeroComparendo
{
    public const int LargoMaximo = 40;

    public static string? Normalizar(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto))
            return null;

        var alfanum = new string(texto.Trim().Where(char.IsAsciiLetterOrDigit).Select(char.ToUpperInvariant).ToArray());
        if (alfanum.Length is 0 or > LargoMaximo)
            return null;

        var i = 0;
        while (i < alfanum.Length && char.IsAsciiLetter(alfanum[i]))
            i++;
        if (i > 0 && i < alfanum.Length && alfanum.Skip(i).All(char.IsAsciiDigit))
            return alfanum[i..];

        return alfanum;
    }

    /// <summary>
    /// HU #13829 (hallazgo B12-4): el OCR a veces lee un dígito de más o de menos en los números nacionales de 20. Se
    /// avisa solo con 19 o 21 dígitos: hay números reales de 13 (formato viejo de Bogotá) y con letras de prefijo.
    /// </summary>
    public static string? AvisoDeLectura(string? texto)
    {
        var digitos = texto?.Count(char.IsAsciiDigit) ?? 0;
        return digitos is 19 or 21
            ? $"El número tiene {digitos} dígitos y los comparendos nacionales tienen 20: compáralo con el documento."
            : null;
    }
}