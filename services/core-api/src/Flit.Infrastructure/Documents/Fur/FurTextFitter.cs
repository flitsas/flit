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

    /// <summary>
    /// Suelo absoluto de cuerpo para <see cref="FitMultiline"/> (HU #11256). Propio de la ruta
    /// multilínea: el <see cref="MinFontRatio"/> de <see cref="Fit"/> daría 4,2–4,7 pt sobre los
    /// cuerpos base 6,5/7,2 de los manifiestos de observaciones y no cumple CF3.
    /// </summary>
    private const double MinMultilineFontSize = 5;

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

    /// <summary>
    /// Encaja el texto de un campo <c>multiline</c> declarado con <c>autoFit: true</c> en el
    /// manifiesto (HU #11256, CF12). <see cref="Fit"/> (campos <c>text</c>, HU #11048) NO se toca —
    /// sigue siendo quien sirve esa ruta en producción — y este método reutiliza sus privados
    /// (<see cref="Wrap"/>, <see cref="MaxLines"/>, <see cref="Truncate"/>) y su misma inyección de
    /// <paramref name="measure"/> para seguir siendo puro y testeable sin PdfSharpCore.
    ///
    /// <para><b>El orden de estrategias es el inverso al de <see cref="Fit"/>, a propósito.</b> En un
    /// campo <c>text</c> (nombre, razón social) se encoge antes de partir: partir un nombre en dos
    /// líneas rompe la calibración de la casilla, que el manifiesto asume de una sola línea. En un
    /// párrafo (observaciones) el recuadro admite varias líneas por diseño, así que la legibilidad
    /// manda: se parte primero y solo se reduce el cuerpo si partir no basta.</para>
    ///
    /// <para><b>Opt-in, no un flag global.</b> Este método solo se invoca cuando el campo declara
    /// <see cref="FurFieldDefinition.AutoFit"/><c> == true</c>. Los sellos de firma
    /// (<c>vehicle_owner_signature</c> / <c>vehicle_buyer_signature</c>) también son <c>multiline</c>
    /// y hoy ya desbordan su caja (automotor: <c>h: 35.3</c> a <c>fontSize: 8</c>, cuatro líneas
    /// ocupan 40 pt) — medirlos con este algoritmo los encogería, una regresión visible en el 100% de
    /// los FUR firmados. Por eso el renderer NUNCA aplica este método por defecto a todo
    /// <c>multiline</c>: solo cuando el manifiesto lo pide explícitamente.</para>
    /// </summary>
    /// <param name="onTruncate">
    /// Se invoca únicamente en el último recurso (paso 4), con el número aproximado de caracteres
    /// elididos. El llamador decide cómo loguearlo (incluye ahí el id del trámite): este método no
    /// depende de ningún framework de logging para seguir siendo puro.
    /// </param>
    internal static FurTextFit FitMultiline(
        string text,
        double maxWidth,
        double maxHeight,
        double baseFontSize,
        Func<string, double, double> measure,
        Action<int>? onTruncate = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(measure);

        // Preprocesado IDÉNTICO al que usa hoy la ruta multiline del renderer sin autoFit: es la
        // garantía de CF4. Si este split divergiera, el passthrough del paso (1) dejaría de reproducir
        // la salida actual.
        var paragraphs = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        // (1) Passthrough — garantía CF4: si ya cabe con el cuerpo declarado, ni una línea ni un punto
        // de cuerpo cambian respecto de hoy.
        if (maxWidth <= 0 || FitsAsIs(paragraphs, maxWidth, maxHeight, baseFontSize, measure))
            return new FurTextFit(paragraphs, baseFontSize);

        // (2) Envolver al cuerpo base (CF2): cada párrafo se envuelve por separado, así que los `\n`
        // explícitos siguen siendo saltos duros. Se exige también que cada línea quepa en el ancho: una
        // palabra suelta más ancha que la caja (sin espacios donde partir) no debe darse por "encajada".
        var atBaseSize = WrapParagraphs(paragraphs, maxWidth, baseFontSize, measure);
        if (atBaseSize.Count <= MaxLines(maxHeight, baseFontSize)
            && atBaseSize.All(l => measure(l, baseFontSize) <= maxWidth))
            return new FurTextFit(atBaseSize, baseFontSize);

        // (3) Reducir el cuerpo re-envolviendo en cada tamaño (CF3): más ancho por línea ⇒ menos
        // líneas. Se acepta el primer tamaño que quepa. El piso (`MinMultilineFontSize`) se prueba
        // siempre de forma explícita al final: los pasos de 0.25 desde `baseFontSize` no necesariamente
        // aterrizan justo en el piso (p. ej. 7.2 → …,5.2,4.95(excluido)), y sin este último intento un
        // texto que sí cabría exactamente a 5 pt caería al truncado del paso (4) sin necesidad.
        for (var size = baseFontSize - Step; size > MinMultilineFontSize; size -= Step)
        {
            var wrapped = WrapParagraphs(paragraphs, maxWidth, size, measure);
            if (wrapped.Count <= MaxLines(maxHeight, size) && wrapped.All(l => measure(l, size) <= maxWidth))
                return new FurTextFit(wrapped, size);
        }

        var atFloor = WrapParagraphs(paragraphs, maxWidth, MinMultilineFontSize, measure);
        if (atFloor.Count <= MaxLines(maxHeight, MinMultilineFontSize)
            && atFloor.All(l => measure(l, MinMultilineFontSize) <= maxWidth))
            return new FurTextFit(atFloor, MinMultilineFontSize);

        // (4) Último recurso, al piso de 5 pt: recorta a las líneas que caben en el alto y trunca la
        // última con elipsis. Pisar los campos vecinos de un formulario oficial es peor que elidir texto.
        return LastResortMultiline(paragraphs, maxWidth, maxHeight, measure, onTruncate);
    }

    /// <summary>Cada párrafo cabe en el ancho al cuerpo declarado y el bloque cabe en el alto.</summary>
    private static bool FitsAsIs(
        string[] paragraphs,
        double maxWidth,
        double maxHeight,
        double fontSize,
        Func<string, double, double> measure)
    {
        if (paragraphs.Length == 0)
            return true;

        if (!paragraphs.All(p => measure(p, fontSize) <= maxWidth))
            return false;

        // Sin alto declarado no hay nada que comprobar: evita dividir por cero (ningún campo real del
        // FUR está en este caso).
        return maxHeight <= 0 || paragraphs.Length * fontSize * LineHeightFactor <= maxHeight;
    }

    /// <summary>Envuelve cada párrafo por separado y concatena: los saltos duros no se mezclan entre sí.</summary>
    private static List<string> WrapParagraphs(
        string[] paragraphs, double maxWidth, double fontSize, Func<string, double, double> measure)
    {
        var lines = new List<string>();
        foreach (var paragraph in paragraphs)
            lines.AddRange(Wrap(paragraph, maxWidth, fontSize, measure));
        return lines;
    }

    private static FurTextFit LastResortMultiline(
        string[] paragraphs,
        double maxWidth,
        double maxHeight,
        Func<string, double, double> measure,
        Action<int>? onTruncate)
    {
        var wrapped = WrapParagraphs(paragraphs, maxWidth, MinMultilineFontSize, measure);
        var maxLines = MaxLines(maxHeight, MinMultilineFontSize);

        var kept = wrapped.Take(maxLines).ToList();
        if (kept.Count == 0)
            kept.Add(string.Empty);

        var overflow = wrapped.Skip(kept.Count).ToList();
        var lastIndex = kept.Count - 1;
        var originalLast = kept[lastIndex];

        // Si se descartaron líneas completas (`overflow`), la última línea visible SIEMPRE debe llevar
        // elipsis, así su propio texto ya quepa en el ancho: de lo contrario el corte queda invisible
        // para quien lee el FUR (parece que el texto simplemente terminó ahí). Si no hubo líneas
        // descartadas, se llegó aquí porque una palabra suelta no cabe ni al piso — ahí sí basta el
        // truncado normal (no fuerza elipsis si por algún motivo ya cupiera).
        var truncatedLast = overflow.Count > 0
            ? ForceEllipsis(originalLast, maxWidth, MinMultilineFontSize, measure)
            : Truncate(originalLast, maxWidth, MinMultilineFontSize, measure);
        kept[lastIndex] = truncatedLast;

        var elidedChars = overflow.Sum(l => l.Length + 1)
            + Math.Max(0, originalLast.Length - Math.Max(0, truncatedLast.Length - Ellipsis.Length));
        if (elidedChars > 0)
            onTruncate?.Invoke(elidedChars);

        return new FurTextFit(kept, MinMultilineFontSize);
    }

    /// <summary>
    /// Como <see cref="Truncate"/>, pero SIEMPRE deja constancia con elipsis, incluso si el texto ya
    /// cabía tal cual. Se usa cuando hay contenido después que no se está mostrando (líneas completas
    /// descartadas): sin esto, la línea visible parecería completa cuando no lo es.
    /// </summary>
    private static string ForceEllipsis(
        string text, double maxWidth, double fontSize, Func<string, double, double> measure)
    {
        var trimmed = text;
        while (trimmed.Length > 0 && measure(trimmed + Ellipsis, fontSize) > maxWidth)
            trimmed = trimmed[..^1];

        return trimmed + Ellipsis;
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
