namespace Flit.Impuestos.Domain.Liquidaciones;

/// <summary>
/// Placa normalizada (S16): mayúsculas, sin espacios ni guiones. Después de normalizar debe quedar entre 1 y
/// <see cref="LargoMaximo"/> letras A-Z o dígitos 0-9 (el largo de <c>liquidacion_items.placa</c>); cualquier otro carácter la
/// invalida. La API responde una placa inválida como 422 <c>PLACA_INVALIDA</c>.
/// </summary>
/// <example>
/// <code>
/// Placa.TryNormalizar(" abc-123 ", out var placa); // true, "ABC123"
/// Placa.Normalizar("ABC.123");                     // ArgumentException
/// </code>
/// </example>
public static class Placa
{
    /// <summary>Largo de la columna <c>liquidacion_items.placa</c>.</summary>
    public const int LargoMaximo = 10;

    /// <summary>La placa normalizada, o false (y vacío) si no es una placa.</summary>
    public static bool TryNormalizar(string? entrada, out string placa)
    {
        placa = string.Empty;
        if (string.IsNullOrWhiteSpace(entrada))
            return false;

        var limpia = new System.Text.StringBuilder(entrada.Length);
        foreach (var c in entrada)
        {
            if (char.IsWhiteSpace(c) || c == '-')
                continue;
            var mayuscula = char.ToUpperInvariant(c);
            if (mayuscula is not (>= 'A' and <= 'Z' or >= '0' and <= '9'))
                return false;
            limpia.Append(mayuscula);
        }

        if (limpia.Length is 0 or > LargoMaximo)
            return false;
        placa = limpia.ToString();
        return true;
    }

    /// <summary>La placa normalizada; <see cref="ArgumentException"/> si no es una placa (el mensaje no repite el valor).</summary>
    public static string Normalizar(string? entrada) =>
        TryNormalizar(entrada, out var placa)
            ? placa
            : throw new ArgumentException($"La placa debe tener de 1 a {LargoMaximo} letras o dígitos (sin contar espacios ni guiones).", nameof(entrada));
}
