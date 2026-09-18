namespace Flit.Admin.Domain.Companies.Branding;

/// <summary>
/// Contraste WCAG 2.1 (criterio 1.4.3): luminancia relativa sRGB → ratio de contraste (HU #12413
/// AC4). Misma fórmula que <c>frontend/lib/brand/contrast.ts</c> (#12414) — fixture compartido en
/// <c>tests/Shared/Fixtures/Branding/contrast-cases.json</c> mantiene ambos lados en paridad.
/// </summary>
public static class WcagContrast
{
    /// <summary>Umbral por defecto WCAG AA para texto normal (parametrizable, AC4).</summary>
    public const double DefaultMinRatio = 4.5;

    /// <summary>Parsea <c>#RRGGBB</c> (con o sin normalizar a mayúsculas) a componentes 0-255.</summary>
    public static (int R, int G, int B) ParseHex(string hex)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hex);
        var value = hex.Trim().TrimStart('#');
        if (value.Length != 6)
        {
            throw new ArgumentException($"Color hexadecimal inválido: \"{hex}\"", nameof(hex));
        }

        return (
            Convert.ToInt32(value[..2], 16),
            Convert.ToInt32(value[2..4], 16),
            Convert.ToInt32(value[4..6], 16));
    }

    private static double ChannelToLinear(int channel)
    {
        var c = channel / 255d;
        return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
    }

    /// <summary>Luminancia relativa WCAG (0..1) de un color <c>#RRGGBB</c>.</summary>
    public static double RelativeLuminance(string hex)
    {
        var (r, g, b) = ParseHex(hex);
        return 0.2126 * ChannelToLinear(r) + 0.7152 * ChannelToLinear(g) + 0.0722 * ChannelToLinear(b);
    }

    /// <summary>Ratio de contraste WCAG entre dos colores <c>#RRGGBB</c> (1..21), sin redondear.</summary>
    public static double ContrastRatio(string hexA, string hexB)
    {
        var l1 = RelativeLuminance(hexA);
        var l2 = RelativeLuminance(hexB);
        var lighter = Math.Max(l1, l2);
        var darker = Math.Min(l1, l2);
        return (lighter + 0.05) / (darker + 0.05);
    }

    /// <summary>Ratio redondeado a 2 decimales — forma en la que se reporta en <c>details.ratio</c> (AC4).</summary>
    public static double RoundedContrastRatio(string hexA, string hexB) =>
        Math.Round(ContrastRatio(hexA, hexB), 2, MidpointRounding.AwayFromZero);

    /// <summary>`true` si el ratio cumple el mínimo (AC4, por defecto 4.5:1, parametrizable).</summary>
    public static bool MeetsMinimum(double ratio, double minRatio = DefaultMinRatio) => ratio >= minRatio;
}
