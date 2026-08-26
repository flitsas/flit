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
/// altera para los textos que ya caben—; (2) se reduce el cuerpo por pasos hasta un mínimo legible
/// —solo hasta donde el encogido sea cosmético si la caja admite más de un renglón, ver
/// <see cref="MaxSingleLineShrinkRatio"/>—; (3) si el alto del campo admite más de una línea, se parte
/// por palabras; (4) como último recurso se trunca con elipsis, que es preferible a pisar el campo
/// de al lado.</para>
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

    /// <summary>
    /// Tope defensivo de entrada de <see cref="FitMultiline"/>. El recuadro más grande del FUR
    /// (remolques, 490×55 pt) rinde ~8 líneas a 5 pt, del orden de 1.500 caracteres: cualquier cosa
    /// por encima de este tope se truncaría igual, así que recortar antes de medir no cambia la salida.
    /// Existe porque el campo de observaciones es texto libre sin límite en el wizard ni en la columna.
    /// </summary>
    private const int MaxMultilineInputChars = 8_000;

    /// <summary>
    /// Reducción máxima admisible en UNA SOLA línea cuando la caja admite más de una (HU sin ADO
    /// 2026-08-12, quinta tanda). El paso (2) encoge antes de partir porque un nombre partido se lee
    /// peor que uno un pelo más pequeño — pero eso solo vale mientras la diferencia sea cosmética.
    ///
    /// <para>Sin este tope, bajar el piso de la casilla 19 de 7,0 a 6,0 no ampliaba el rango de
    /// nombres completos: lo que hacía era ENCOGER los medianos. Medido con la fuente embebida,
    /// "DISTRIBUIDORA NACIONAL DE CARGA" pasaba de 7,60pt en 2 líneas a 6,10pt en 1, un 20% menos de
    /// cuerpo en un recuadro con sitio para tres renglones. Con el tope, el paso (2) se detiene antes
    /// y el paso (3) parte el texto conservando el cuerpo declarado; el piso de 6,0 queda para lo que
    /// de verdad lo necesita: los nombres que ni partidos caben (el caso del RUES que motivó la
    /// tanda), donde la alternativa era truncar.</para>
    ///
    /// <para>0,92 reproduce a propósito la calibración anterior de la casilla 19 (piso 7,0 sobre
    /// cuerpo 7,6 = 0,921): 7,6 → 7,0 en un renglón es imperceptible al imprimir, 7,6 → 6,1 no.</para>
    /// </summary>
    private const double MaxSingleLineShrinkRatio = 0.92;

    /// <summary>Paso de reducción del cuerpo.</summary>
    private const double Step = 0.25;

    /// <summary>Factor de alto de línea, igual que el del renderer.</summary>
    private const double LineHeightFactor = 1.25;

    private const string Ellipsis = "…";

    /// <summary>
    /// La elipsis que marca un truncado, expuesta para que el renderer pueda DETECTARLO en la ruta de
    /// campos `text`, que no admite callback (HU #11643). Debe seguir siendo la misma en las dos rutas
    /// de encaje: si divergieran, el aviso de truncado dejaría de dispararse en silencio.
    /// </summary>
    internal const string EllipsisChar = Ellipsis;

    /// <param name="measure">Mide el ancho de un texto para un cuerpo dado.</param>
    /// <param name="minFontSize">
    /// HU sin ADO 2026-08-11 (tercera tanda, casilla 19 del FUR) — piso ABSOLUTO opcional, distinto
    /// del piso por defecto (<see cref="MinFontRatio"/> × <paramref name="baseFontSize"/>). Un campo
    /// declarado a un cuerpo bajo (p. ej. 7,6 pt) tiene por defecto un piso de ~4,9 pt (0,65×7,6):
    /// suficiente para ganar caracteres antes de truncar, pero por debajo de lo legible en un
    /// formulario que se imprime y a veces se escanea. Este parámetro deja fijar un piso mayor
    /// (p. ej. 6,5 pt) SIN tocar <see cref="MinFontRatio"/>, que es global y calibra el resto de
    /// campos de texto del FUR. <c>null</c> (por defecto) conserva el comportamiento de siempre.
    /// </param>
    internal static FurTextFit Fit(
        string text,
        double maxWidth,
        double maxHeight,
        double baseFontSize,
        Func<string, double, double> measure,
        double? minFontSize = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(measure);

        // Sin ancho declarado no hay nada que encajar (campos sin `w` en el manifiesto).
        if (maxWidth <= 0)
            return new FurTextFit([text], baseFontSize);

        // (1) Cabe tal cual: no se toca nada.
        if (measure(text, baseFontSize) <= maxWidth)
            return new FurTextFit([text], baseFontSize);

        var minFont = minFontSize is { } floor
            ? Math.Max(MinFontSize, floor)
            : Math.Max(MinFontSize, baseFontSize * MinFontRatio);

        // (2) Reducir el cuerpo manteniendo una sola línea. Con una caja de más de un renglón el
        // encogido en una sola línea se limita a lo cosmético (ver MaxSingleLineShrinkRatio): pasado
        // ese punto es el paso (3) —partir conservando el cuerpo declarado— el que da mejor resultado.
        var singleLineFloor = MaxLines(maxHeight, minFont) >= 2
            ? Math.Max(minFont, baseFontSize * MaxSingleLineShrinkRatio)
            : minFont;

        for (var size = baseFontSize - Step; size >= singleLineFloor; size -= Step)
        {
            if (measure(text, size) <= maxWidth)
                return new FurTextFit([text], size);
        }

        // HU sin ADO 2026-08-11 (cuarta tanda) — el piso puede no coincidir con la rejilla de pasos de
        // 0,25 desde `baseFontSize` (p. ej. base 7,6 / piso 7,0: el bucle de arriba prueba 7,35 y 7,10
        // y se detiene en 6,85 sin tocar el 7,0 exacto). Sin este intento explícito, un texto que SÍ
        // cabría en una sola línea justo al piso caía al paso (3) y se partía en líneas sin necesidad.
        // Mismo motivo por el que `FitMultiline` ya prueba su propio piso de forma explícita. El piso
        // que se prueba aquí es el de UNA línea, que con una caja multilínea es más alto que `minFont`.
        if (measure(text, singleLineFloor) <= maxWidth)
            return new FurTextFit([text], singleLineFloor);

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

        // Mismo motivo que el intento explícito del paso (2): el piso puede no coincidir con la
        // rejilla de 0,25 desde `baseFontSize`, así que se prueba una última vez exactamente en
        // `minFont` antes de rendirse al truncado. Con un piso alto (p. ej. 7,0) esto puede ser la
        // diferencia entre 3 líneas completas y truncar de más por probar solo hasta ~7,1.
        if (MaxLines(maxHeight, minFont) >= 2)
        {
            var wrappedAtFloor = Wrap(text, maxWidth, minFont, measure);
            if (wrappedAtFloor.Count <= MaxLines(maxHeight, minFont)
                && wrappedAtFloor.All(l => measure(l, minFont) <= maxWidth))
                return new FurTextFit(wrappedAtFloor, minFont);
        }

        // (4) Último recurso, al piso: si el alto admite varias líneas se aprovechan TODAS antes de
        // truncar (recorta a las que caben y trunca solo la última con elipsis) — HU sin ADO
        // 2026-08-11 (cuarta tanda): con un campo de una sola línea (h chico) esto degrada exactamente
        // al truncado de siempre (maxLinesAtFloor < 2). Con `h` para varias líneas (p. ej. casilla 19),
        // desperdiciar la 2ª/3ª línea aquí habría sido peor que necesario: "cuando de verdad no cabe,
        // trunca" no significa "trunca ya en la primera línea si hay más espacio abajo".
        var maxLinesAtFloor = MaxLines(maxHeight, minFont);
        if (maxLinesAtFloor < 2)
            return new FurTextFit([Truncate(text, maxWidth, minFont, measure)], minFont);

        var overflowWrap = Wrap(text, maxWidth, minFont, measure);
        var kept = overflowWrap.Take(maxLinesAtFloor).ToList();
        if (kept.Count == 0)
            kept.Add(string.Empty);

        var hasOverflow = overflowWrap.Count > kept.Count;
        var lastIdx = kept.Count - 1;
        kept[lastIdx] = hasOverflow
            ? ForceEllipsis(kept[lastIdx], maxWidth, minFont, measure)
            : Truncate(kept[lastIdx], maxWidth, minFont, measure);

        return new FurTextFit(kept, minFont);
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
    /// (<c>vehicle_owner_signature</c> / <c>vehicle_buyer_signature</c>) también son <c>multiline</c>,
    /// y aunque hoy no desbordan —el mapper les aplica un <c>FontSizeDelta</c> negativo que baja el
    /// cuerpo efectivo— medirlos con este algoritmo los recalcularía: una regresión visible en el 100%
    /// de los FUR firmados. Por eso el renderer NUNCA aplica este método por defecto a todo
    /// <c>multiline</c>: solo cuando el manifiesto lo pide explícitamente.</para>
    /// </summary>
    /// <param name="onTruncate">
    /// Se invoca cuando se pierde contenido: en el último recurso (paso 4) y también si la guarda de
    /// entrada recortó el texto. Recibe el número aproximado de caracteres elididos. El llamador
    /// decide cómo loguearlo (incluye ahí el id del trámite): este método no depende de ningún
    /// framework de logging para seguir siendo puro.
    /// </param>
    /// <param name="minFontSize">
    /// HU sin ADO 2026-08-11 (cuarta tanda, casilla 19 del FUR) — piso ABSOLUTO opcional, análogo al
    /// que ya recibe <see cref="Fit"/>. Reemplaza el piso por defecto (<see cref="MinMultilineFontSize"/>,
    /// 5 pt — calibrado para <c>observations</c>, un párrafo libre donde priorizar caracteres visibles
    /// importa más que el cuerpo) por uno mayor (p. ej. 7 pt) cuando el campo necesita mantenerse
    /// legible en un formulario que se imprime y a veces se escanea. <c>null</c> (por defecto)
    /// conserva el piso de 5 pt de siempre — <c>observations</c> no pasa este parámetro y no cambia.
    /// </param>
    internal static FurTextFit FitMultiline(
        string text,
        double maxWidth,
        double maxHeight,
        double baseFontSize,
        Func<string, double, double> measure,
        Action<int>? onTruncate = null,
        double? minFontSize = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(measure);

        var floor = minFontSize ?? MinMultilineFontSize;

        // Guarda de entrada. `observations` es texto libre y no tiene tope ni en el wizard ni en la
        // columna, así que puede llegar un pegado de cientos de miles de caracteres. Recortar aquí no
        // cambia la salida —el recuadro más grande del FUR rinde del orden de 1.500 caracteres, así
        // que todo lo que pase del tope se elidiría igual— y evita pasear esa cadena por el envolvido
        // y el truncado. Lo recortado se suma a los caracteres reportados: no se pierde en silencio.
        if (text.Length <= MaxMultilineInputChars)
            return FitMultilineCore(text, maxWidth, maxHeight, baseFontSize, measure, onTruncate, floor);

        var elidedByGuard = text.Length - MaxMultilineInputChars;
        var reported = false;
        var fit = FitMultilineCore(
            text[..MaxMultilineInputChars], maxWidth, maxHeight, baseFontSize, measure,
            elided =>
            {
                reported = true;
                onTruncate?.Invoke(elided + elidedByGuard);
            },
            floor);

        if (!reported)
            onTruncate?.Invoke(elidedByGuard);

        return fit;
    }

    private static FurTextFit FitMultilineCore(
        string text,
        double maxWidth,
        double maxHeight,
        double baseFontSize,
        Func<string, double, double> measure,
        Action<int>? onTruncate,
        double floor)
    {

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
        // líneas. Se acepta el primer tamaño que quepa. El piso (`floor`) se prueba siempre de forma
        // explícita al final: los pasos de 0.25 desde `baseFontSize` no necesariamente aterrizan justo
        // en el piso (p. ej. 7.2 → …,5.2,4.95(excluido)), y sin este último intento un texto que sí
        // cabría exactamente al piso caería al truncado del paso (4) sin necesidad.
        for (var size = baseFontSize - Step; size > floor; size -= Step)
        {
            var wrapped = WrapParagraphs(paragraphs, maxWidth, size, measure);
            if (wrapped.Count <= MaxLines(maxHeight, size) && wrapped.All(l => measure(l, size) <= maxWidth))
                return new FurTextFit(wrapped, size);
        }

        var atFloor = WrapParagraphs(paragraphs, maxWidth, floor, measure);
        if (atFloor.Count <= MaxLines(maxHeight, floor) && atFloor.All(l => measure(l, floor) <= maxWidth))
            return new FurTextFit(atFloor, floor);

        // (4) Último recurso, al piso: recorta a las líneas que caben en el alto y trunca la última con
        // elipsis. Pisar los campos vecinos de un formulario oficial es peor que elidir texto — y NUNCA
        // baja del piso, aunque eso signifique truncar más de lo que un cuerpo menor habría permitido.
        return LastResortMultiline(paragraphs, maxWidth, maxHeight, measure, onTruncate, floor);
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
        Action<int>? onTruncate,
        double floor)
    {
        var wrapped = WrapParagraphs(paragraphs, maxWidth, floor, measure);
        var maxLines = MaxLines(maxHeight, floor);

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
            ? ForceEllipsis(originalLast, maxWidth, floor, measure)
            : Truncate(originalLast, maxWidth, floor, measure);
        kept[lastIndex] = truncatedLast;

        var elidedChars = overflow.Sum(l => l.Length + 1)
            + Math.Max(0, originalLast.Length - Math.Max(0, truncatedLast.Length - Ellipsis.Length));
        if (elidedChars > 0)
            onTruncate?.Invoke(elidedChars);

        return new FurTextFit(kept, floor);
    }

    /// <summary>
    /// Como <see cref="Truncate"/>, pero SIEMPRE deja constancia con elipsis, incluso si el texto ya
    /// cabía tal cual. Se usa cuando hay contenido después que no se está mostrando (líneas completas
    /// descartadas): sin esto, la línea visible parecería completa cuando no lo es.
    /// </summary>
    private static string ForceEllipsis(
        string text, double maxWidth, double fontSize, Func<string, double, double> measure)
        => EllipsizeToWidth(text, maxWidth, fontSize, minKept: 0, measure);

    /// <summary>
    /// Mayor prefijo de <paramref name="text"/> que, con la elipsis pegada, cabe en
    /// <paramref name="maxWidth"/>; nunca conserva menos de <paramref name="minKept"/> caracteres.
    ///
    /// <para><b>Por qué búsqueda binaria y no quitar un carácter y remedir.</b> El descenso lineal es
    /// O(N²) sobre un token sin espacios: cada vuelta copia la cadena entera y la vuelve a medir glifo
    /// a glifo. Con las observaciones —texto libre, sin tope de longitud— un pegado de 100.000
    /// caracteres seguidos suponía del orden de 5.000 millones de glifos medidos y dejaba un core al
    /// 100% durante minutos, con la generación del FUR bloqueada de forma síncrona. El predicado
    /// «cabe en el ancho» es monótono respecto del índice de corte, así que la binaria es exacta:
    /// devuelve el mismo prefijo que el descenso lineal, en O(log N) mediciones.</para>
    /// </summary>
    private static string EllipsizeToWidth(
        string text, double maxWidth, double fontSize, int minKept, Func<string, double, double> measure)
    {
        if (measure(text + Ellipsis, fontSize) <= maxWidth || text.Length <= minKept)
            return text + Ellipsis;

        // Invariante: `lo` es un corte aceptable (cabe, o es el mínimo exigido) y `hi` no cabe.
        var lo = minKept;
        var hi = text.Length;
        while (hi - lo > 1)
        {
            var mid = lo + ((hi - lo) / 2);
            if (measure(text[..mid] + Ellipsis, fontSize) <= maxWidth)
                lo = mid;
            else
                hi = mid;
        }

        return text[..lo] + Ellipsis;
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

        // Mismo prefijo que el descenso lineal anterior, sin su coste cuadrático. Ver EllipsizeToWidth.
        return EllipsizeToWidth(text, maxWidth, fontSize, minKept: 1, measure);
    }
}
