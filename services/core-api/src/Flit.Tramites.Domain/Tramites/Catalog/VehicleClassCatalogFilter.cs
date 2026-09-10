using System.Globalization;
using System.Text;

namespace Flit.Tramites.Domain.Tramites.Catalog;

/// <summary>
/// Normaliza la clase RUNT del vehículo y la empata con las claves del catálogo de carrocerías.
/// </summary>
public static class VehicleClassCatalogFilter
{
    private static readonly Dictionary<string, string> SpellingAliases = new(StringComparer.Ordinal)
    {
        ["SEMIRREMOLQUE"] = "SEMIREMOLQUE",
    };

    public static string Normalize(string? raw)
    {
        var value = (raw ?? string.Empty).Trim();
        if (value.Length == 0) return string.Empty;

        var decomposed = value.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (var ch in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
                sb.Append(ch);
        }

        var folded = sb.ToString().Normalize(NormalizationForm.FormC).ToUpperInvariant();
        folded = folded
            .Replace('Á', 'A').Replace('É', 'E').Replace('Í', 'I').Replace('Ó', 'O').Replace('Ú', 'U')
            .Replace('Ü', 'U');
        return string.Join(' ', folded.Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>
    /// Empata la clase consultada con una clave del catálogo (exacta, alias o prefijo de palabra:
    /// <c>CAMION CISTERNA</c> → <c>CAMION</c>). <c>null</c> si no hay match.
    /// </summary>
    public static string? MatchKnownClass(string vehicleClass, IReadOnlyCollection<string> knownClasses)
    {
        var key = Normalize(vehicleClass);
        if (key.Length == 0) return null;

        var index = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var original in knownClasses)
        {
            var n = Normalize(original);
            if (n.Length == 0 || index.ContainsKey(n)) continue;
            index[n] = original.Trim();
        }

        if (index.TryGetValue(key, out var exact)) return exact;
        if (SpellingAliases.TryGetValue(key, out var alias) && index.TryGetValue(alias, out var aliased))
            return aliased;

        var longest = index.Keys
            .OrderByDescending(k => k.Length)
            .FirstOrDefault(k => key == k || key.StartsWith(k + " ", StringComparison.Ordinal));
        return longest is null ? null : index[longest];
    }
}
