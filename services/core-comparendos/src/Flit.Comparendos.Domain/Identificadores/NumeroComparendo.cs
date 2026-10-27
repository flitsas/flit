namespace Flit.Comparendos.Domain.Identificadores;

/// <summary>
/// Llave de unicidad del comparendo por empresa (RN-021, decisión de P4): sin espacios a los lados, en mayúsculas y
/// solo letras y dígitos. Las letras de prefijo se conservan («d-123 456» → «D123456»).
/// </summary>
public static class NumeroComparendo
{
    public const int LargoMaximo = 40;

    public static string? Normalizar(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto))
            return null;

        var limpio = new string(texto.Trim().Where(char.IsAsciiLetterOrDigit).Select(char.ToUpperInvariant).ToArray());
        return limpio.Length is > 0 and <= LargoMaximo ? limpio : null;
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
