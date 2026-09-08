using System.Globalization;
using System.Text;
using Flit.Tramites.Application.Documents;

namespace Flit.Infrastructure.Documents;

/// <summary>
/// Texto de trazabilidad de la firma del baúl (ADR-0025 §4). Delega el formato en
/// <see cref="FirmaBaulSelloText"/> (Application) para no divergir con la impronta manual.
/// </summary>
internal static class FlitFirmaBaulSello
{
    internal static string Build(FirmaBaulMetadata meta, bool incluirIdentificacion) =>
        FirmaBaulSelloText.Build(meta, incluirIdentificacion);

    internal static string? Resolve(
        IReadOnlyDictionary<string, FirmaBaulMetadata>? metadatos,
        string? rol,
        bool incluirIdentificacion)
    {
        if (metadatos is null || string.IsNullOrWhiteSpace(rol))
            return null;

        foreach (var key in RolKeys(rol))
        {
            if (metadatos.TryGetValue(key, out var meta))
                return Build(meta, incluirIdentificacion);
        }

        return null;
    }

    internal static IEnumerable<string> RolKeys(string rol)
    {
        var n = Norm(rol);
        yield return rol;
        if (n.Contains("COMPRADOR", StringComparison.Ordinal)) yield return "comprador";
        if (n.Contains("VENDEDOR", StringComparison.Ordinal)) yield return "vendedor";
        if (n.Contains("PROPIETARIO", StringComparison.Ordinal)) yield return "propietario";
    }

    private static string Norm(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return string.Empty;
        var decomposed = s.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                sb.Append(c);
        }

        return sb.ToString().ToUpperInvariant().Trim();
    }
}
