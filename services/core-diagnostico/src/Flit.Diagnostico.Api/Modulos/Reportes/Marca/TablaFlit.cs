using System.Globalization;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace Flit.Diagnostico.Api.Modulos.Reportes.Marca;

/// <summary>
/// Tablas y recuadro de nota del diseño v2 del PDF de Diagnóstico (ajuste 2026-10-27): fila de encabezado con degradado horizontal
/// <see cref="FlitDocumentTheme.Turquesa"/> → <see cref="FlitDocumentTheme.PrimaryBlue"/> que recorre toda la fila, texto blanco
/// Poppins Medium centrado y esquinas superiores redondeadas solo en los extremos; filas de datos en
/// <see cref="FlitDocumentTheme.FondoFila"/> con texto gris centrado, separadores blancos de 1 pt (el hueco entre celdas deja ver la
/// página) y las esquinas inferiores de la última fila redondeadas. QuestPDF 2024.12.3 no tiene degradados ni corner-radius: cada
/// celda pinta su fondo con un SVG <b>dinámico</b> (<c>Svg(size =&gt; …)</c>) del tamaño real de la celda, como capa de fondo bajo
/// el texto (la capa principal). Así la celda crece con su texto (dos líneas) sin deformar las esquinas, a diferencia del SVG de alto
/// fijo de <c>FlitRoundedCells</c> de core-api. El degradado de la fila se reparte por celda: cada una va del color interpolado en su
/// borde izquierdo al de su borde derecho, de modo que la fila entera se lee como una sola banda.
/// </summary>
/// <example>
/// <code>
/// col.Item().Element(c => TablaFlit.Campos(c, [("Placa", "ABC123"), ("VIN", "9BW…"), ("Servicio", "PARTICULAR"), ("Clase", "AUTOMOVIL")]));
/// col.Item().Element(c => TablaFlit.Grilla(c, ["Vigencia", "Valor", "Estado"], [["2025", "$ 180.000", "Pagada"]]));
/// col.Item().Element(c => TablaFlit.Nota(c, "La información que se encuentra en este documento…"));
/// </code>
/// </example>
internal static class TablaFlit
{
    /// <summary>Columnas por bloque de las tablas etiqueta-valor (fila de etiquetas y debajo la de valores).</summary>
    public const int ColumnasCampos = 3;

    /// <summary>Radio de las esquinas de las tablas (pt).</summary>
    public const float Radio = 6;

    /// <summary>Separador blanco entre celdas y filas (pt).</summary>
    public const float Separador = 1;

    /// <summary>Tamaño del texto de las tablas y de la nota (pt).</summary>
    public const float TamanoTexto = 9.5f;

    /// <summary>Radio de las esquinas del recuadro de la nota (pt).</summary>
    public const float RadioNota = 12;

    /// <summary>Relleno horizontal de cada celda, a cada lado (pt).</summary>
    public const float RellenoCelda = 5;

    /// <summary>Tamaño mínimo al que <see cref="Distribuir"/> reduce el texto del encabezado de una grilla ancha (pt).</summary>
    public const float TamanoMinimoEncabezado = 7.5f;

    /// <summary>Valores de estado que se pintan en verde (comparación sin mayúsculas).</summary>
    public static readonly IReadOnlySet<string> EstadosFavorables = new HashSet<string>(["Vigente", "Aprobada", "Pagada"], StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Tabla etiqueta-valor en bloques de <see cref="ColumnasCampos"/> columnas: una fila de etiquetas (encabezado degradado) y debajo
    /// la de sus valores, repetido. Si sobran 1 o 2 campos al final, la última pareja de filas ocupa solo esas columnas (a la
    /// izquierda). Cada pareja no se parte entre páginas. Con <paramref name="centrar"/>, una tabla de un solo bloque con menos de
    /// <see cref="ColumnasCampos"/> campos queda centrada con el ancho de columna de siempre (<see cref="MargenCentrado"/>).
    /// </summary>
    public static void Campos(IContainer contenedor, IReadOnlyList<(string Etiqueta, string Valor)> campos, bool centrar = false)
    {
        ArgumentNullException.ThrowIfNull(campos);
        Campos(contenedor, [.. campos.Select(c => (c.Etiqueta, c.Valor, 1))], centrar);
    }

    /// <summary>
    /// Como <see cref="Campos(IContainer, IReadOnlyList{ValueTuple{string, string}})"/>, pero cada campo ocupa <c>Columnas</c>
    /// columnas del bloque (1 a <see cref="ColumnasCampos"/>; p. ej. «Organismo de tránsito» a dos columnas en el diseño v2). Un
    /// campo que no cabe en lo que queda del bloque abre el siguiente. <paramref name="centrar"/>: ver
    /// <see cref="Campos(IContainer, IReadOnlyList{ValueTuple{string, string}}, bool)"/>.
    /// </summary>
    public static void Campos(IContainer contenedor, IReadOnlyList<(string Etiqueta, string Valor, int Columnas)> campos, bool centrar = false)
    {
        ArgumentNullException.ThrowIfNull(contenedor);
        ArgumentNullException.ThrowIfNull(campos);
        var bloques = Bloques(campos);
        var ocupadas = bloques.Count == 1 ? bloques[0].Sum(x => x.Columnas) : ColumnasCampos;
        var margen = centrar ? MargenCentrado(ocupadas, bloques.Count) : 0;
        if (margen > 0)
        {
            // Rejilla de solo las columnas ocupadas, con el ancho de columna de la de 3 y el mismo margen a cada lado.
            contenedor.Row(fila =>
            {
                fila.RelativeItem(margen);
                fila.RelativeItem(ocupadas).Element(c => BloquesDeCampos(c, bloques, ocupadas));
                fila.RelativeItem(margen);
            });
            return;
        }

        BloquesDeCampos(contenedor, bloques, ColumnasCampos);
    }

    /// <summary>
    /// Margen (en columnas de la rejilla de <see cref="ColumnasCampos"/>) a cada lado de una tabla etiqueta-valor centrada: solo con
    /// un bloque de menos de <see cref="ColumnasCampos"/> columnas ocupadas (2 campos ⇒ media columna; 1 ⇒ una). Con varios bloques
    /// (p. ej. SIMIT y su «Cancelada» sola al final) o un bloque lleno, 0: la tabla ocupa todo el ancho como siempre.
    /// </summary>
    internal static float MargenCentrado(int columnasOcupadas, int bloques) =>
        bloques == 1 && columnasOcupadas is > 0 and < ColumnasCampos ? (ColumnasCampos - columnasOcupadas) / 2f : 0;

    private static void BloquesDeCampos(IContainer contenedor, List<List<(string Etiqueta, string Valor, int Columnas)>> bloques, int totalColumnas) =>
        contenedor.Column(col =>
        {
            col.Spacing(Separador);
            for (var b = 0; b < bloques.Count; b++)
            {
                var bloque = bloques[b];
                var primero = b == 0;
                var ultimo = b == bloques.Count - 1;
                col.Item().ShowEntire().Column(par =>
                {
                    par.Spacing(Separador);
                    par.Item().Element(c => Fila(c, [.. bloque.Select(x => (x.Etiqueta, x.Columnas))], totalColumnas, encabezado: true, arriba: primero, abajo: false));
                    par.Item().Element(c => Fila(c, [.. bloque.Select(x => (x.Valor, x.Columnas))], totalColumnas, encabezado: false, arriba: false, abajo: ultimo));
                });
            }
        });

    /// <summary>
    /// Grilla: encabezado degradado (se repite si la tabla se parte entre páginas) y una fila por elemento (nunca partida). Sin filas
    /// y con <paramref name="sinFilas"/>, una sola fila con ese texto que abarca todas las columnas. <paramref name="anchos"/>
    /// (opcional, uno por columna, enteros ≥ 1): ancho relativo de cada columna; sin él, todas iguales (<see cref="Distribuir"/> los
    /// calcula para que ninguna palabra se parta). <paramref name="tamanoEncabezado"/> (opcional): tamaño del texto del encabezado;
    /// sin él, <see cref="TamanoTexto"/>.
    /// </summary>
    public static void Grilla(
        IContainer contenedor, IReadOnlyList<string> columnas, IReadOnlyList<string[]> filas, string? sinFilas = null, IReadOnlyList<int>? anchos = null,
        float? tamanoEncabezado = null)
    {
        ArgumentNullException.ThrowIfNull(contenedor);
        ArgumentNullException.ThrowIfNull(columnas);
        ArgumentNullException.ThrowIfNull(filas);
        if (anchos is not null && (anchos.Count != columnas.Count || anchos.Any(a => a < 1)))
            throw new ArgumentException("Un ancho entero ≥ 1 por columna.", nameof(anchos));
        var pesos = anchos ?? [.. columnas.Select(_ => 1)];
        var total = pesos.Sum();
        List<IReadOnlyList<(string, int)>> datos = filas.Count == 0 && sinFilas is not null
            ? [[(sinFilas, total)]]
            : filas.Select(f => (IReadOnlyList<(string, int)>)[.. f.Select((t, i) => (t, i < pesos.Count ? pesos[i] : 1))]).ToList();
        contenedor.Table(tabla =>
        {
            tabla.ColumnsDefinition(c => c.RelativeColumn());
            tabla.Header(h => h.Cell().Element(c => Fila(c, [.. columnas.Select((t, i) => (t, pesos[i]))], total, encabezado: true, arriba: true, abajo: datos.Count == 0,
                tamanoEncabezado ?? TamanoTexto)));
            for (var i = 0; i < datos.Count; i++)
            {
                var fila = datos[i];
                var ultima = i == datos.Count - 1;
                tabla.Cell().PaddingTop(Separador).ShowEntire().Element(c => Fila(c, fila, total, encabezado: false, arriba: false, abajo: ultima));
            }
        });
    }

    /// <summary>
    /// Tabla de dos columnas: la etiqueta a la izquierda (cabecera) y el valor a la derecha. Una fila por par, sin partirse.
    /// <paramref name="pesoEtiqueta"/>/<paramref name="pesoValor"/> (opcionales): ancho relativo de cada columna; sin ellos, mitad y mitad.
    /// </summary>
    public static void Pares(IContainer contenedor, IReadOnlyList<(string Etiqueta, string Valor)> filas, float pesoEtiqueta = 1, float pesoValor = 1)
    {
        ArgumentNullException.ThrowIfNull(contenedor);
        ArgumentNullException.ThrowIfNull(filas);
        contenedor.Column(col =>
        {
            col.Spacing(Separador);
            for (var i = 0; i < filas.Count; i++)
            {
                var arriba = i == 0;
                var abajo = i == filas.Count - 1;
                var (etiqueta, valor) = filas[i];
                col.Item().ShowEntire().Row(fila =>
                {
                    fila.Spacing(Separador);
                    fila.RelativeItem(pesoEtiqueta).Element(c => Pintar(c, etiqueta, encabezado: true, new Esquinas(arriba, false, false, abajo), 0, 1));
                    fila.RelativeItem(pesoValor).Element(c => Pintar(c, valor, encabezado: false, new Esquinas(false, arriba, abajo, false), 0, 1));
                });
            }
        });
    }

    /// <summary>Una sola fila de dato a todo el ancho, con las cuatro esquinas redondeadas (p. ej. «No registra» de una tabla etiqueta-valor sin datos).</summary>
    public static void SinDatos(IContainer contenedor, string texto)
    {
        ArgumentNullException.ThrowIfNull(contenedor);
        Fila(contenedor, [(texto, 1)], 1, encabezado: false, arriba: true, abajo: true);
    }

    /// <summary>Reparte los campos en bloques de <see cref="ColumnasCampos"/> columnas según lo que ocupa cada uno.</summary>
    private static List<List<(string Etiqueta, string Valor, int Columnas)>> Bloques(IReadOnlyList<(string Etiqueta, string Valor, int Columnas)> campos)
    {
        var bloques = new List<List<(string Etiqueta, string Valor, int Columnas)>>();
        var ocupadas = ColumnasCampos;
        foreach (var campo in campos)
        {
            var ancho = Math.Clamp(campo.Columnas, 1, ColumnasCampos);
            if (ocupadas + ancho > ColumnasCampos)
            {
                bloques.Add([]);
                ocupadas = 0;
            }

            bloques[^1].Add((campo.Etiqueta, campo.Valor, ancho));
            ocupadas += ancho;
        }

        return bloques;
    }

    /// <summary>
    /// Recuadro NOTA: borde azul de 1 pt con esquinas de <see cref="RadioNota"/> pt sobre fondo lila (SVG dinámico, así que admite
    /// cualquier alto), icono «i» en círculo azul + «NOTA» y el texto justificado en azul oscuro. No se parte entre páginas.
    /// </summary>
    public static void Nota(IContainer contenedor, string texto)
    {
        ArgumentNullException.ThrowIfNull(contenedor);
        contenedor.ShowEntire().Layers(capas =>
        {
            capas.Layer().Svg(tamano => RecuadroSvg(tamano.Width, tamano.Height));
            capas.PrimaryLayer().PaddingVertical(14).PaddingHorizontal(18).Column(col =>
            {
                col.Spacing(6);
                col.Item().Row(fila =>
                {
                    fila.Spacing(6);
                    fila.ConstantItem(13).AlignMiddle().Width(13).Height(13).Svg(IconoInfoSvg);
                    fila.AutoItem().AlignMiddle().Text("NOTA").FontFamily(FlitDocumentTheme.FontRegular).Bold().FontSize(11)
                        .FontColor(FlitDocumentTheme.PrimaryBlue);
                });
                col.Item().Text(texto).FontSize(TamanoTexto).FontColor(FlitDocumentTheme.DarkNavy).Justify();
            });
        });
    }

    /// <summary>
    /// Una fila de <paramref name="totalColumnas"/> columnas de igual ancho con las <paramref name="celdas"/> en las primeras, cada
    /// una ocupando sus <c>Columnas</c> (las sobrantes quedan vacías, sin fondo). <paramref name="arriba"/>/<paramref name="abajo"/>
    /// redondean las esquinas de los extremos de lo ocupado. <paramref name="tamanoEncabezado"/>: tamaño del texto si es encabezado.
    /// </summary>
    private static void Fila(
        IContainer contenedor, IReadOnlyList<(string Texto, int Columnas)> celdas, int totalColumnas, bool encabezado, bool arriba, bool abajo,
        float tamanoEncabezado = TamanoTexto) =>
        contenedor.Row(fila =>
        {
            fila.Spacing(Separador);
            var inicio = 0;
            for (var i = 0; i < celdas.Count; i++)
            {
                var (texto, columnas) = celdas[i];
                var esquinas = new Esquinas(
                    Tl: arriba && i == 0, Tr: arriba && i == celdas.Count - 1,
                    Br: abajo && i == celdas.Count - 1, Bl: abajo && i == 0);
                var desde = (float)inicio / totalColumnas;
                var hasta = (float)(inicio + columnas) / totalColumnas;
                inicio += columnas;
                fila.RelativeItem(columnas).Element(c => Pintar(c, texto, encabezado, esquinas, desde, hasta, tamanoEncabezado));
            }

            for (var i = inicio; i < totalColumnas; i++)
                fila.RelativeItem();
        });

    /// <summary>Una celda: fondo SVG del tamaño real y el texto centrado encima.</summary>
    private static void Pintar(
        IContainer contenedor, string texto, bool encabezado, Esquinas esquinas, float desde, float hasta, float tamanoEncabezado = TamanoTexto) =>
        contenedor.Layers(capas =>
        {
            capas.Layer().Svg(tamano => CeldaSvg(tamano.Width, tamano.Height, esquinas, encabezado, desde, hasta));
            var celda = capas.PrimaryLayer().PaddingVertical(4).PaddingHorizontal(RellenoCelda).AlignCenter().AlignMiddle().Text(texto)
                .FontSize(encabezado ? tamanoEncabezado : TamanoTexto);
            if (encabezado)
                celda.FontFamily(FlitDocumentTheme.FontMedium).FontColor(FlitDocumentTheme.Blanco);
            else
                celda.FontColor(EstadosFavorables.Contains(texto.Trim()) ? FlitDocumentTheme.Verde : FlitDocumentTheme.TextoTabla);
        });

    /// <summary>Esquinas redondeadas de una celda (superior izquierda, superior derecha, inferior derecha, inferior izquierda).</summary>
    internal readonly record struct Esquinas(bool Tl, bool Tr, bool Br, bool Bl);

    // SVG dinámico (CeldaSvg, RecuadroSvg, Envolver): solo números y colores del tema. Nunca interpolar texto de la fuente; si algún
    // día entra, escaparlo antes con SecurityElement.Escape.

    /// <summary>
    /// SVG del fondo de una celda de <paramref name="ancho"/> × <paramref name="alto"/>: degradado entre el color de la fila en
    /// <paramref name="desde"/> y en <paramref name="hasta"/> (fracciones del ancho de la fila) si es encabezado, o lila si es dato.
    /// </summary>
    internal static string CeldaSvg(float ancho, float alto, Esquinas esquinas, bool encabezado, float desde, float hasta)
    {
        var ruta = Ruta(0, 0, ancho, alto, Math.Min(Radio, alto / 2), esquinas);
        if (!encabezado)
            return Envolver(ancho, alto, $"<path d=\"{ruta}\" fill=\"{FlitDocumentTheme.FondoFila}\"/>");
        var degradado = "<defs><linearGradient id=\"g\" x1=\"0\" y1=\"0\" x2=\"1\" y2=\"0\">"
            + $"<stop offset=\"0\" stop-color=\"{Interpolar(desde)}\"/><stop offset=\"1\" stop-color=\"{Interpolar(hasta)}\"/></linearGradient></defs>";
        return Envolver(ancho, alto, degradado + $"<path d=\"{ruta}\" fill=\"url(#g)\"/>");
    }

    /// <summary>SVG del recuadro de la nota: borde azul de 1 pt (dentro del área) y fondo lila, esquinas de <see cref="RadioNota"/>.</summary>
    internal static string RecuadroSvg(float ancho, float alto)
    {
        const float media = 0.5f;
        var todas = new Esquinas(true, true, true, true);
        var ruta = Ruta(media, media, ancho - media, alto - media, Math.Min(RadioNota, (alto - 1) / 2), todas);
        return Envolver(ancho, alto,
            $"<path d=\"{ruta}\" fill=\"{FlitDocumentTheme.FondoFila}\" stroke=\"{FlitDocumentTheme.PrimaryBlue}\" stroke-width=\"1\"/>");
    }

    /// <summary>Icono «i» blanco en círculo azul (punto + trazo), sin texto SVG para no depender de fuentes.</summary>
    internal const string IconoInfoSvg =
        "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"16\" height=\"16\" viewBox=\"0 0 16 16\">"
        + "<circle cx=\"8\" cy=\"8\" r=\"8\" fill=\"" + FlitDocumentTheme.PrimaryBlue + "\"/>"
        + "<circle cx=\"8\" cy=\"4.6\" r=\"1.25\" fill=\"#FFFFFF\"/>"
        + "<rect x=\"6.9\" y=\"6.8\" width=\"2.2\" height=\"5.8\" rx=\"1.1\" fill=\"#FFFFFF\"/></svg>";

    /// <summary>Color del degradado de la fila en la fracción <paramref name="t"/> (0 = turquesa, 1 = azul FLIT).</summary>
    internal static string Interpolar(float t)
    {
        t = Math.Clamp(t, 0, 1);
        static (int R, int G, int B) Rgb(string hex) => (
            int.Parse(hex.AsSpan(1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
            int.Parse(hex.AsSpan(3, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
            int.Parse(hex.AsSpan(5, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
        var (r0, g0, b0) = Rgb(FlitDocumentTheme.Turquesa);
        var (r1, g1, b1) = Rgb(FlitDocumentTheme.PrimaryBlue);
        int Mezcla(int a, int b) => (int)Math.Round(a + ((b - a) * t));
        return string.Create(CultureInfo.InvariantCulture, $"#{Mezcla(r0, r1):X2}{Mezcla(g0, g1):X2}{Mezcla(b0, b1):X2}");
    }

    /// <summary>Ruta de un rectángulo con las <paramref name="esquinas"/> marcadas redondeadas con radio <paramref name="r"/>.</summary>
    private static string Ruta(float x0, float y0, float x1, float y1, float r, Esquinas esquinas)
    {
        r = Math.Max(0, Math.Min(r, Math.Min(x1 - x0, y1 - y0) / 2));
        float tl = esquinas.Tl ? r : 0, tr = esquinas.Tr ? r : 0, br = esquinas.Br ? r : 0, bl = esquinas.Bl ? r : 0;
        return string.Create(CultureInfo.InvariantCulture,
            $"M {x0 + tl} {y0} H {x1 - tr} A {tr} {tr} 0 0 1 {x1} {y0 + tr} V {y1 - br} A {br} {br} 0 0 1 {x1 - br} {y1} "
            + $"H {x0 + bl} A {bl} {bl} 0 0 1 {x0} {y1 - bl} V {y0 + tl} A {tl} {tl} 0 0 1 {x0 + tl} {y0} Z");
    }

    private static string Envolver(float ancho, float alto, string cuerpo) => string.Create(CultureInfo.InvariantCulture,
        $"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{ancho}\" height=\"{alto}\" viewBox=\"0 0 {ancho} {alto}\">{cuerpo}</svg>");
    // Anchos de avance de Poppins (milésimas de em), leídos de Marca/Assets/Poppins-*.ttf, para los caracteres de Glifos. Las vocales
    // tildadas, la ü y la ñ tienen el mismo avance que su letra base. Un carácter desconocido cuenta como AnchoDesconocido (holgado).
    private const string Glifos = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789.,:;/-()$%#&'\" º–*+_";

    private static readonly short[] AvanceRegular =
    [
        676, 676, 607, 676, 620, 329, 676, 640, 246, 248, 515, 246, 1030, 640, 640, 676, 676, 373, 522, 364, 640, 561, 820, 479, 563, 455,
        674, 613, 772, 707, 513, 504, 778, 692, 246, 530, 599, 432, 861, 703, 786, 579, 788, 608, 587, 541, 675, 676, 976, 621, 584, 541,
        628, 320, 575, 589, 629, 628, 635, 546, 631, 630, 210, 198, 213, 264, 476, 551, 454, 454, 622, 759, 840, 739, 159, 292, 267, 436,
        677, 486, 683, 733,
    ];

    private static readonly short[] AvanceMedium =
    [
        678, 678, 600, 678, 617, 332, 678, 649, 264, 264, 554, 264, 1039, 649, 638, 678, 678, 384, 534, 372, 649, 576, 825, 499, 582, 471,
        697, 630, 773, 709, 524, 515, 773, 705, 264, 564, 633, 444, 882, 721, 784, 595, 787, 632, 604, 564, 690, 694, 999, 660, 605, 558,
        641, 350, 577, 594, 647, 639, 642, 560, 638, 635, 241, 225, 245, 304, 514, 585, 492, 492, 650, 794, 872, 761, 172, 323, 260, 444,
        708, 504, 714, 798,
    ];

    private const short AnchoDesconocido = 800;

    /// <summary>Holgura multiplicativa sobre el ancho estimado de una palabra (redondeos del motor).</summary>
    private const float Holgura = 1.04f;

    /// <summary>Holgura fija sobre el ancho estimado de una palabra (pt).</summary>
    private const float HolguraFija = 2;

    /// <summary>
    /// Ancho estimado (pt) de <paramref name="texto"/> en una línea, en Poppins Regular (o Medium, la de los encabezados) a
    /// <paramref name="tamano"/> pt, sumando el avance de cada carácter. QuestPDF no expone la medición de texto.
    /// </summary>
    internal static float AnchoTexto(string texto, float tamano, bool medium = false)
    {
        ArgumentNullException.ThrowIfNull(texto);
        var avances = medium ? AvanceMedium : AvanceRegular;
        var milesimas = 0;
        foreach (var caracter in texto)
        {
            var i = Glifos.IndexOf(Base(caracter), StringComparison.Ordinal);
            milesimas += i >= 0 ? avances[i] : AnchoDesconocido;
        }

        return milesimas * tamano / 1000f;
    }

    /// <summary>Ancho estimado (pt) de la palabra más ancha de <paramref name="texto"/> (las líneas solo se parten en los espacios).</summary>
    internal static float AnchoPalabraMayor(string texto, float tamano, bool medium = false)
    {
        ArgumentNullException.ThrowIfNull(texto);
        return texto.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(p => AnchoTexto(p, tamano, medium)).DefaultIfEmpty(0).Max();
    }

    /// <summary>Ancho (pt) de una celda para que <paramref name="texto"/> quepa en una línea: texto con holgura más el relleno.</summary>
    internal static float AnchoCelda(string texto, float tamano, bool medium = false) => Ajustar(AnchoTexto(texto, tamano, medium));

    /// <summary>
    /// Anchos relativos de las columnas de una grilla de <paramref name="anchoDisponible"/> pt para que ninguna palabra (del
    /// encabezado en Medium ni de las celdas en Regular <see cref="TamanoTexto"/>) quede más ancha que su celda: cada columna recibe
    /// al menos su palabra más ancha más el relleno; si así no caben todas, el encabezado baja de 0,5 en 0,5 pt hasta
    /// <see cref="TamanoMinimoEncabezado"/>. Lo que sobra se reparte como en una tabla HTML automática: si todos los textos caben en
    /// una línea, cada columna su texto más una parte igual; si no, en proporción a lo que a cada columna le falta para su texto
    /// entero. Devuelve enteros (décimas de pt) para <see cref="Grilla"/>.
    /// </summary>
    internal static DistribucionGrilla Distribuir(IReadOnlyList<string> columnas, IReadOnlyList<string[]> filas, float anchoDisponible)
    {
        ArgumentNullException.ThrowIfNull(columnas);
        ArgumentNullException.ThrowIfNull(filas);
        var n = columnas.Count;
        var util = anchoDisponible - ((n - 1) * Separador);

        var tamanoEncabezado = TamanoTexto;
        var minimos = Minimos(columnas, filas, tamanoEncabezado);
        while (minimos.Sum() > util && tamanoEncabezado > TamanoMinimoEncabezado)
        {
            tamanoEncabezado -= 0.5f;
            minimos = Minimos(columnas, filas, tamanoEncabezado);
        }

        var naturales = new float[n];
        for (var i = 0; i < n; i++)
        {
            var texto = Math.Max(AnchoTexto(columnas[i], tamanoEncabezado, medium: true), Celdas(filas, i).Select(c => AnchoTexto(c, TamanoTexto)).DefaultIfEmpty(0).Max());
            naturales[i] = Math.Max(minimos[i], Ajustar(texto));
        }

        float[] anchos;
        if (naturales.Sum() <= util)
        {
            var extra = (util - naturales.Sum()) / n;
            anchos = [.. naturales.Select(x => x + extra)];
        }
        else if (minimos.Sum() < util)
        {
            var falta = naturales.Zip(minimos, (a, b) => a - b).ToArray();
            var extra = util - minimos.Sum();
            anchos = [.. minimos.Select((m, i) => m + (extra * falta[i] / falta.Sum()))];
        }
        else
        {
            anchos = minimos;
        }

        return new DistribucionGrilla([.. anchos.Select(a => Math.Max(1, (int)Math.Round(a * 10)))], tamanoEncabezado);
    }

    /// <summary>Por columna: su palabra más ancha (encabezado en Medium a <paramref name="tamanoEncabezado"/>, celdas en Regular) más el relleno.</summary>
    private static float[] Minimos(IReadOnlyList<string> columnas, IReadOnlyList<string[]> filas, float tamanoEncabezado) =>
    [
        .. columnas.Select((columna, i) => Ajustar(Math.Max(
            AnchoPalabraMayor(columna, tamanoEncabezado, medium: true),
            Celdas(filas, i).Select(c => AnchoPalabraMayor(c, TamanoTexto)).DefaultIfEmpty(0).Max()))),
    ];

    private static IEnumerable<string> Celdas(IReadOnlyList<string[]> filas, int columna) =>
        filas.Where(f => columna < f.Length).Select(f => f[columna] ?? string.Empty);

    private static float Ajustar(float anchoTexto) => (anchoTexto * Holgura) + HolguraFija + (2 * RellenoCelda);

    /// <summary>Anchos relativos de las columnas (enteros ≥ 1) y tamaño del texto del encabezado de una grilla.</summary>
    internal sealed record DistribucionGrilla(IReadOnlyList<int> Anchos, float TamanoEncabezado);

    private static char Base(char caracter) => caracter switch
    {
        'á' => 'a', 'é' => 'e', 'í' => 'i', 'ó' => 'o', 'ú' or 'ü' => 'u', 'ñ' => 'n',
        'Á' => 'A', 'É' => 'E', 'Í' => 'I', 'Ó' => 'O', 'Ú' or 'Ü' => 'U', 'Ñ' => 'N',
        _ => caracter,
    };
}
