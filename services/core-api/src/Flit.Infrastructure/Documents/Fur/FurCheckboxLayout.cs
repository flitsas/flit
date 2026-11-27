namespace Flit.Infrastructure.Documents.Fur;

public readonly record struct FurXPlacement(double FontSize, double LeftX, double BaselineY);

/// <summary>
/// Centra las X de tipo de documento dentro de la casilla del blank (Size del manifiesto).
/// Con 2–4 copropietarios la altura se divide en franjas iguales, una X por franja.
/// x/y del manifiesto de una casilla son la esquina superior izquierda de una casilla cuadrada
/// size×size y su centro debe coincidir con el centro de la casilla impresa. Las proporciones
/// GlyphWidthRatio=0.771 y CapHeightRatio=0.729 se midieron en DejaVuSans-Bold.ttf (avance de la X
/// y altura de mayúscula).
/// </summary>
public static class FurCheckboxLayout
{
    /// <summary>Tamaño de casilla por defecto cuando el manifiesto trae size &lt;= 0.</summary>
    public const double DefaultBoxSize = 9;

    public const double GlyphWidthRatio = 0.771;

    public const double CapHeightRatio = 0.729;

    private const double StripFill = 0.9;

    public static IReadOnlyList<FurXPlacement> Place(double x, double y, double size, int count, double singleFontSize)
    {
        var n = Math.Clamp(count, 1, 4);
        var s = size > 0 ? size : DefaultBoxSize;
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
