namespace Flit.Consultas.Api.Impuesto;

/// <summary>
/// Montos del servicio de impuesto. El punto y la coma son separadores de miles: <c>1.234</c> es 1234.
/// El vacío no afirma nada y el negativo no es un monto.
/// </summary>
internal static class LectorMontoImpuesto
{
    public static long? Leer(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto))
            return null;
        var limpio = texto.Replace('\u00a0', ' ').Trim();
        if (limpio.StartsWith('$'))
            limpio = limpio[1..].Trim();
        if (limpio.EndsWith("COP", StringComparison.OrdinalIgnoreCase))
            limpio = limpio[..^3].Trim();
        if (limpio.Length == 0 || limpio.Contains('-'))
            return null;
        if (limpio.Contains('.') && limpio.Contains(','))
            return null;

        var separador = limpio.Contains('.') ? '.' : limpio.Contains(',') ? ',' : '\0';
        var digitos = separador == '\0' ? limpio : SinSeparador(limpio, separador);
        if (digitos is null || digitos.Length == 0 || !SoloDigitos(digitos))
            return null;
        return long.TryParse(digitos, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var valor)
            ? valor
            : null;
    }

    private static string? SinSeparador(string texto, char separador)
    {
        var partes = texto.Split(separador);
        if (partes.Length < 2 || partes[0].Length is < 1 or > 3 || !SoloDigitos(partes[0]))
            return null;
        for (var i = 1; i < partes.Length; i++)
        {
            if (partes[i].Length != 3 || !SoloDigitos(partes[i]))
                return null;
        }

        return string.Concat(partes);
    }

    private static bool SoloDigitos(string texto)
    {
        foreach (var caracter in texto)
        {
            if (!char.IsDigit(caracter))
                return false;
        }

        return true;
    }
}
