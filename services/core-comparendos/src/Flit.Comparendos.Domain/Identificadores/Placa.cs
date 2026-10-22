namespace Flit.Comparendos.Domain.Identificadores;

/// <summary>Placa vehicular monitoreada: mayúsculas, sin espacios ni guiones («abc-123» → «ABC123»).</summary>
public static class Placa
{
    public static string? Normalizar(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto))
            return null;

        var limpia = new string(texto.Where(char.IsAsciiLetterOrDigit).Select(char.ToUpperInvariant).ToArray());
        return limpia.Length is >= 5 and <= 7 ? limpia : null;
    }
}
