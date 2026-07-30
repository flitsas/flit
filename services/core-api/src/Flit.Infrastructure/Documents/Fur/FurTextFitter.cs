namespace Flit.Infrastructure.Documents.Fur;

/// <summary>Resultado de encajar un texto en la caja de un campo del FUR.</summary>
/// <param name="Lines">Líneas a dibujar (una sola si no hizo falta partir el texto).</param>
/// <param name="FontSize">Cuerpo con el que caben.</param>
internal sealed record FurTextFit(IReadOnlyList<string> Lines, double FontSize);

/// <summary>
/// Encaja el texto de un campo de tipo <c>text</c> en su caja declarada (HU #11048).
///
/// <para><b>El defecto.</b> El overlay dibujaba siempre con el cuerpo del manifiesto, sin mirar el ancho
/// del campo, así que una razón social larga se salía del recuadro del FUR y se montaba sobre los campos
/// vecinos. El caso real: el campo de nombre del propietario declara ~93 pt de ancho y 7,7 pt de cuerpo.</para>
///
/// <para><b>La estrategia</b>, en este orden, para tocar lo mínimo: (1) si cabe con el cuerpo declarado
/// se deja EXACTAMENTE como está —la calibración del manifiesto en milímetros de la HU #10921 no se
/// altera para los textos que ya caben—; (2) se reduce el cuerpo por pasos hasta un mínimo legible;
/// (3) si el alto del campo admite más de una línea, se parte por palabras; (4) como último recurso se
/// trunca con elipsis, que es preferible a pisar el campo de al lado.</para>
///
/// <para>La medición se inyecta (<c>measure</c>) para que el algoritmo sea puro y testeable sin
/// PdfSharpCore ni una fuente instalada.</para>
/// </summary>
internal static class FurTextFitter
{
    /// <summary>Cuerpo mínimo como fracción del declarado: por debajo el formulario deja de ser legible.</summary>
    private const double MinFontRatio = 0.65;

    /// <summary>Suelo absoluto de cuerpo, en puntos (mismo que usa el renderer).</summary>
    private const double MinFontSize = 3;

    /// <summary>Paso de reducción del cuerpo.</summary>
    private const double Step = 0.25;

    /// <summary>Factor de alto de línea, igual que el del renderer.</summary>
    private const double LineHeightFactor = 1.25;

    private const string Ellipsis = "…";

    /// <param name="measure">Mide el ancho de un texto para un cuerpo dado.</param>
    internal static FurTextFit Fit(
        string text,
        double maxWidth,
        double maxHeight,
        double baseFontSize,
        Func<string, double, double> measure)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(measure);

        // Sin ancho declarado no hay nada que encajar (campos sin `w` en el manifiesto).
        if (maxWidth <= 0)
            return new FurTextFit([text], baseFontSize);

        // (1) Cabe tal cual: no se toca nada.
        if (measure(text, baseFontSize) <= maxWidth)
            return new FurTextFit([text], baseFontSize);

        var minFont = Math.Max(MinFontSize, baseFontSize * MinFontRatio);

        // (2) Reducir el cuerpo manteniendo una sola línea.
        for (var size = baseFontSize - Step; size >= minFont; size -= Step)
        {
            if (measure(text, size) <= maxWidth)
                return new FurTextFit([text], size);
        }

        // (3) Partir por palabras, si el alto admite más de una línea. Se prueba de mayor a menor cuerpo
        //     para conservar la mayor legibilidad posible.
        for (var size = baseFontSize; size >= minFont; size -= Step)
        {
            var maxLines = MaxLines(maxHeight, size);
            if (maxLines < 2)
                continue;

            var wrapped = Wrap(text, maxWidth, size, measure);
            if (wrapped.Count <= maxLines && wrapped.All(l => measure(l, size) <= maxWidth))
                return new FurTextFit(wrapped, size);
        }

        // (4) Último recurso: una línea al cuerpo mínimo, truncada con elipsis.
        return new FurTextFit([Truncate(text, maxWidth, minFont, measure)], minFont);
    }

    /// <summary>Líneas que caben en el alto del campo con un cuerpo dado.</summary>
    private static int MaxLines(double maxHeight, double fontSize)
    {
        if (maxHeight <= 0)
            return 1;
        return Math.Max(1, (int)Math.Floor(maxHeight / (fontSize * LineHeightFactor)));
    }

    /// <summary>
    /// Parte el texto por palabras. Una palabra más ancha que la caja se deja sola en su línea: el
    /// llamador decide si con ese reparto se conforma (y si no, se trunca en el paso 4).
    /// </summary>
    private static List<string> Wrap(
        string text, double maxWidth, double fontSize, Func<string, double, double> measure)
    {
        var lines = new List<string>();
        var current = string.Empty;

        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = current.Length == 0 ? word : $"{current} {word}";
            if (measure(candidate, fontSize) <= maxWidth)
            {
                current = candidate;
                continue;
            }

            if (current.Length > 0)
                lines.Add(current);
            current = word;
        }

        if (current.Length > 0)
            lines.Add(current);

        return lines.Count == 0 ? [text] : lines;
    }

    private static string Truncate(
        string text, double maxWidth, double fontSize, Func<string, double, double> measure)
    {
        if (measure(text, fontSize) <= maxWidth)
            return text;

        var trimmed = text;
        while (trimmed.Length > 1 && measure(trimmed + Ellipsis, fontSize) > maxWidth)
            trimmed = trimmed[..^1];

        return trimmed + Ellipsis;
    }
}
