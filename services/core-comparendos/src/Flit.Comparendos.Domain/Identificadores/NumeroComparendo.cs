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
}
