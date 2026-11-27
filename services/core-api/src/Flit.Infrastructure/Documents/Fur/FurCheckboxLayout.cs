namespace Flit.Infrastructure.Documents.Fur;

public readonly record struct FurXPlacement(double FontSize, double LeftX, double BaselineY);

/// <summary>
/// Centra las X de tipo de documento dentro de la casilla del blank (Size del manifiesto).
/// Con 2–4 copropietarios la altura se divide en franjas iguales, una X por franja.
/// </summary>
public static class FurCheckboxLayout
{
    public const double GlyphWidthRatio = 0.771;

    public const double CapHeightRatio = 0.729;

    private const double StripFill = 0.9;

    public static IReadOnlyList<FurXPlacement> Place(double x, double y, double size, int count, double singleFontSize)
    {
        var n = Math.Clamp(count, 1, 4);
        var s = size > 0 ? size : 9;
        var strip = s / n;

        var fontSize = n == 1
            ? singleFontSize
            : Math.Min(strip * StripFill / CapHeightRatio, s * StripFill / GlyphWidthRatio);

        var left = x + s / 2 - GlyphWidthRatio * fontSize / 2;
        var result = new FurXPlacement[n];
        for (var i = 0; i < n; i++)
        {
            var centerY = y + strip * (i + 0.5);
            result[i] = new FurXPlacement(fontSize, left, centerY + CapHeightRatio * fontSize / 2);
        }

        return result;
    }
}
