namespace Flit.Infrastructure.Documents.Fur;

/// <summary>
/// Apila las X de tipo de documento dentro del recuadro del blank (Size del manifiesto).
/// Con 2–4 copropietarios el cuerpo y el paso se reducen para no pisar Dirección.
/// </summary>
public static class FurCheckboxLayout
{
    /// <summary>Descenso estimado de la “X” bajo la línea base (fracción del cuerpo).</summary>
    public const double DescentRatio = 0.12;

    /// <summary>Subida visual pedida sobre el recuadro (Y crece hacia abajo).</summary>
    public const double VisualLift = 4;

    /// <summary>Incremento de cuerpo sobre el tamaño que cabe en el recuadro.</summary>
    public const double FontBump = 2;

    public static (double FontSize, double FirstBaseline, double Step) Stack(
        double fieldY,
        double boxH,
        int count,
        double singleFontSize)
    {
        var n = Math.Clamp(count, 1, 4);
        var h = boxH > 0 ? boxH : 9;
        if (n == 1)
            return (singleFontSize + FontBump, fieldY + h * 0.85 - VisualLift, 0);

        var fontSize = Math.Max(1.8, h / (n * 1.08));
        var step = fontSize * 0.92;
        var first = fieldY + fontSize * 0.78;
        var lastBottom = first + step * (n - 1) + fontSize * DescentRatio;
        var overflow = lastBottom - (fieldY + h);
        if (overflow > 0)
            first -= overflow;
        if (first < fieldY + fontSize * 0.55)
            first = fieldY + fontSize * 0.55;

        return (fontSize + FontBump, first - VisualLift, step);
    }
}
