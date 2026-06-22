using System.Text.RegularExpressions;

namespace Flit.Tramites.Domain.Tramites.Services;

/// <summary>
/// Normalización de documentos y validación de email (paridad <c>normalizarDocumentoTraspaso</c> /
/// <c>normalizarEmailTraspaso</c> / <c>emailTraspasoValido</c> de Johan). Puro.
/// </summary>
public static partial class TramiteDocumento
{
    /// <summary>Quita espacios, puntos y guiones de un documento.</summary>
    public static string Normalizar(string? doc) =>
        string.IsNullOrEmpty(doc) ? string.Empty : SeparadoresRegex().Replace(doc.Trim(), string.Empty);

    /// <summary>Normaliza un email (trim + minúsculas).</summary>
    public static string NormalizarEmail(string? email) =>
        (email ?? string.Empty).Trim().ToLowerInvariant();

    /// <summary>¿El email tiene forma válida básica?</summary>
    public static bool EmailValido(string? email)
    {
        var e = (email ?? string.Empty).Trim();
        return e.Length > 0 && EmailRegex().IsMatch(e);
    }

    [GeneratedRegex(@"[\s.\-]")]
    private static partial Regex SeparadoresRegex();

    [GeneratedRegex(@"^[^\s@]+@[^\s@]+\.[^\s@]+$")]
    private static partial Regex EmailRegex();
}
