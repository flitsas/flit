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
}
