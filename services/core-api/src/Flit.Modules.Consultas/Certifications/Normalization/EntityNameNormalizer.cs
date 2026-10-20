using System.Text;

namespace Flit.Tramites.Domain.Certifications.Normalization;

/// <summary>
/// Normaliza nombres de entidad que van impresos en un certificado: aseguradora, CDA, razón social,
/// cámara de comercio.
/// </summary>
/// <remarks>
/// Solo se toca lo que es ruido de transporte —espacios de relleno, comillas envolventes que algunos
/// proveedores agregan al serializar, saltos de línea dentro del nombre—. <b>No se cambia la
/// capitalización</b>: "SEGUROS DEL ESTADO S.A." es como aparece en la póliza y como debe salir en el
/// certificado; "arreglarlo" a Title Case produciría un documento que no coincide con el original.
/// </remarks>
public static class EntityNameNormalizer
{
    /// <summary>Longitud máxima persistida. Por encima se trunca conservando el crudo completo.</summary>
    public const int MaxLength = 400;

    public static CertifiedName Normalize(string? raw)
    {
        var collapsed = Collapse(raw);
        if (collapsed is null)
            return new CertifiedName(null, raw);

        if (collapsed.Length > MaxLength)
            collapsed = collapsed[..MaxLength].TrimEnd();

        return new CertifiedName(collapsed, raw);
    }

    /// <summary>Trim + comillas envolventes fuera + espacios internos (incluidos saltos de línea) colapsados a uno.</summary>
    internal static string? Collapse(string? raw)
    {
        var text = raw?.Trim();
        if (string.IsNullOrEmpty(text))
            return null;

        text = StripWrappingQuotes(text);
        if (text.Length == 0)
            return null;

        var builder = new StringBuilder(text.Length);
        var lastWasSpace = false;

        foreach (var ch in text)
        {
            if (char.IsWhiteSpace(ch))
            {
                if (lastWasSpace || builder.Length == 0)
                    continue;
                builder.Append(' ');
                lastWasSpace = true;
                continue;
            }

            builder.Append(ch);
            lastWasSpace = false;
        }

        while (builder.Length > 0 && builder[^1] == ' ')
            builder.Length--;

        return builder.Length == 0 ? null : builder.ToString();
    }

    /// <summary>Quita comillas simples o dobles que envuelvan TODO el texto, repetidamente.</summary>
    private static string StripWrappingQuotes(string text)
    {
        while (text.Length >= 2
               && ((text[0] == '"' && text[^1] == '"') || (text[0] == '\'' && text[^1] == '\'')))
        {
            text = text[1..^1].Trim();
        }

        return text;
    }
}
