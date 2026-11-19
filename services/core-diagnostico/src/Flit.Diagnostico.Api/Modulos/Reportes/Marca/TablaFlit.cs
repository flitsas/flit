using System.Globalization;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace Flit.Diagnostico.Api.Modulos.Reportes.Marca;

/// <summary>
/// Tablas del diseño v2 del PDF de Diagnóstico (ajuste 2026-10-27): fila de encabezado con degradado horizontal
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

    /// <summary>Tamaño del texto de las tablas (pt).</summary>
    public const float TamanoTexto = 9.5f;

    /// <summary>Relleno horizontal de cada celda, a cada lado (pt).</summary>
    public const float RellenoCelda = 5;


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
    /// calcula para que ninguna palabra se parta). <paramref name="tamanoEncabezado"/>, <paramref name="tamanoCuerpo"/> y
    /// <paramref name="relleno"/> (opcionales): tamaño del texto del encabezado y de las filas, y relleno horizontal de las celdas; sin
    /// ellos, <see cref="TamanoTexto"/> y <see cref="RellenoCelda"/>.
    /// </summary>
    public static void Grilla(
        IContainer contenedor, IReadOnlyList<string> columnas, IReadOnlyList<string[]> filas, string? sinFilas = null, IReadOnlyList<int>? anchos = null,
        float? tamanoEncabezado = null, float? tamanoCuerpo = null, float? relleno = null)
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
                tamanoEncabezado ?? TamanoTexto, tamanoCuerpo ?? TamanoTexto, relleno ?? RellenoCelda)));
            for (var i = 0; i < datos.Count; i++)
            {
                var fila = datos[i];
                var ultima = i == datos.Count - 1;
                tabla.Cell().PaddingTop(Separador).ShowEntire().Element(c => Fila(c, fila, total, encabezado: false, arriba: false, abajo: ultima,
                    tamanoEncabezado ?? TamanoTexto, tamanoCuerpo ?? TamanoTexto, relleno ?? RellenoCelda));
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
    /// Una fila de <paramref name="totalColumnas"/> columnas de igual ancho con las <paramref name="celdas"/> en las primeras, cada
    /// una ocupando sus <c>Columnas</c> (las sobrantes quedan vacías, sin fondo). <paramref name="arriba"/>/<paramref name="abajo"/>
    /// redondean las esquinas de los extremos de lo ocupado. <paramref name="tamanoEncabezado"/>/<paramref name="tamanoCuerpo"/>:
    /// tamaño del texto si es encabezado o fila; <paramref name="relleno"/>: relleno horizontal de las celdas.
    /// </summary>
    private static void Fila(
        IContainer contenedor, IReadOnlyList<(string Texto, int Columnas)> celdas, int totalColumnas, bool encabezado, bool arriba, bool abajo,
        float tamanoEncabezado = TamanoTexto, float tamanoCuerpo = TamanoTexto, float relleno = RellenoCelda) =>
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
                fila.RelativeItem(columnas).Element(c => Pintar(c, texto, encabezado, esquinas, desde, hasta, tamanoEncabezado, tamanoCuerpo, relleno));
            }

            for (var i = inicio; i < totalColumnas; i++)
                fila.RelativeItem();
        });

    /// <summary>Una celda: fondo SVG del tamaño real y el texto centrado encima.</summary>
    private static void Pintar(
        IContainer contenedor, string texto, bool encabezado, Esquinas esquinas, float desde, float hasta, float tamanoEncabezado = TamanoTexto,
        float tamanoCuerpo = TamanoTexto, float relleno = RellenoCelda) =>
        contenedor.Layers(capas =>
        {
            capas.Layer().Svg(tamano => CeldaSvg(tamano.Width, tamano.Height, esquinas, encabezado, desde, hasta));
            var celda = capas.PrimaryLayer().PaddingVertical(4).PaddingHorizontal(relleno).AlignCenter().AlignMiddle().Text(texto)
                .FontSize(encabezado ? tamanoEncabezado : tamanoCuerpo);
            if (encabezado)
                celda.FontFamily(FlitDocumentTheme.FontMedium).FontColor(FlitDocumentTheme.Blanco);
            else
                celda.FontColor(EstadosFavorables.Contains(texto.Trim()) ? FlitDocumentTheme.Verde : FlitDocumentTheme.TextoTabla);
        });

    /// <summary>Esquinas redondeadas de una celda (superior izquierda, superior derecha, inferior derecha, inferior izquierda).</summary>
    internal readonly record struct Esquinas(bool Tl, bool Tr, bool Br, bool Bl);

    // SVG dinámico (CeldaSvg, Envolver): solo números y colores del tema. Nunca interpolar texto de la fuente; si algún
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

    /// <summary>Holgura multiplicativa sobre el ancho estimado del texto (sin interletrado: el motor nunca pinta más ancho).</summary>
    private const float Holgura = 1.01f;

    /// <summary>Holgura fija sobre el ancho estimado del texto de una celda (pt).</summary>
    private const float HolguraFija = 1.5f;

    /// <summary>Tamaños del cuerpo que prueba <see cref="Distribuir"/>, de mayor a menor (como mucho 1 pt menos que el de siempre).</summary>
    private static readonly float[] TamanosCuerpo = [TamanoTexto, TamanoTexto - 0.5f, TamanoTexto - 1];

    /// <summary>Rellenos horizontales que prueba <see cref="Distribuir"/>, de mayor a menor.</summary>
    private static readonly float[] Rellenos = [RellenoCelda, RellenoCelda - 1, RellenoCelda - 2];

    /// <summary>Fracción máxima del ancho útil que reserva el mínimo de una columna cuando ni así caben todas (una palabra enorme).</summary>
    private const float TopeMinimo = 0.5f;

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

    /// <summary>Ancho (pt) de una celda con el relleno de siempre para que <paramref name="texto"/> quepa en una línea.</summary>
    internal static float AnchoCelda(string texto, float tamano, bool medium = false) => Exterior(AnchoTexto(texto, tamano, medium) * Holgura, RellenoCelda);

    /// <summary>
    /// Líneas que ocupa <paramref name="texto"/> en una columna de <paramref name="anchoColumna"/> pt con <paramref name="relleno"/> a
    /// cada lado, partiendo solo en los espacios como el motor. Si una palabra no cabe, <see cref="int.MaxValue"/> (se partiría).
    /// </summary>
    internal static int LineasEnColumna(string texto, float anchoColumna, float tamano, float relleno, bool medium = false) =>
        Lineas(texto, Interior(anchoColumna, relleno), tamano, medium);

    /// <summary>
    /// Anchos relativos de las columnas de una grilla de <paramref name="anchoDisponible"/> pt con el encabezado a
    /// <paramref name="tamanoEncabezado"/> pt (fijo: un solo tamaño en todo el documento). Ninguna palabra queda más ancha que su
    /// celda y el sobrante va a donde quita líneas:
    /// <list type="number">
    /// <item>Cada columna arranca en su palabra más ancha (encabezado o cuerpo) más el relleno.</item>
    /// <item>Mientras sobre ancho, se lo da a la columna cuyo siguiente corte de línea quita más líneas por punto: primero baja las
    /// filas más altas (una fila mide lo que su celda más alta) y luego las celdas sueltas. Así el texto largo (organismo, entidad)
    /// recibe el ancho y las columnas de fechas o números no se quedan con sobrante.</item>
    /// <item>Prueba el cuerpo a 9,5 / 9 / 8,5 pt y el relleno a 5 / 4 / 3 pt; elige el que deja en una línea los textos cortos
    /// (hasta 3 palabras y 14 caracteres: «No hay datos», «No vigente», fechas), sin reducir el cuerpo si no hace falta, y con menos
    /// líneas.</item>
    /// <item>Si ni así caben las palabras (una palabra enorme), cada mínimo se limita a la mitad del ancho útil: solo se parte esa
    /// palabra, no los encabezados ajenos.</item>
    /// </list>
    /// Devuelve enteros (décimas de pt) para <see cref="Grilla"/> con el cuerpo y el relleno elegidos.
    /// </summary>
    internal static DistribucionGrilla Distribuir(
        IReadOnlyList<string> columnas, IReadOnlyList<string[]> filas, float anchoDisponible, float tamanoEncabezado = TamanoTexto)
    {
        ArgumentNullException.ThrowIfNull(columnas);
        ArgumentNullException.ThrowIfNull(filas);
        var util = anchoDisponible - ((columnas.Count - 1) * Separador);
        Reparto? elegido = null;
        foreach (var cuerpo in TamanosCuerpo)
        {
            foreach (var relleno in Rellenos)
            {
                var reparto = Repartir(columnas, filas, util, tamanoEncabezado, cuerpo, relleno);
                if (reparto is not null && (elegido is null || reparto.Clave.CompareTo(elegido.Clave) < 0))
                    elegido = reparto;
            }
        }

        var anchos = elegido?.Anchos ?? Limitados(columnas, filas, util, tamanoEncabezado);
        return new DistribucionGrilla(
            [.. anchos.Select(a => Math.Max(1, (int)Math.Round(a * 10)))], tamanoEncabezado, elegido?.Cuerpo ?? TamanoTexto, elegido?.Relleno ?? RellenoCelda);
    }

    /// <summary>Anchos relativos de las columnas (enteros ≥ 1), tamaño del encabezado y del cuerpo y relleno de las celdas de una grilla.</summary>
    internal sealed record DistribucionGrilla(IReadOnlyList<int> Anchos, float TamanoEncabezado, float TamanoCuerpo, float Relleno);

    /// <summary>
    /// Clave de comparación de un reparto (menor es mejor): textos cortos partidos, cuerpo reducido o no, líneas de las filas y
    /// líneas de las celdas. Los empates conservan el primero probado (cuerpo y relleno mayores).
    /// </summary>
    private sealed record Reparto(float[] Anchos, float Cuerpo, float Relleno, (int Cortos, int Reducido, int Filas, int Celdas) Clave);

    /// <summary>Un texto de una columna con sus cortes: <c>Cortes[k]</c> = ancho interior mínimo para caber en k + 1 líneas.</summary>
    private sealed record Texto(int Fila, bool Corto, float[] Cortes);

    private static Reparto? Repartir(IReadOnlyList<string> columnas, IReadOnlyList<string[]> filas, float util, float encabezado, float cuerpo, float relleno)
    {
        var n = columnas.Count;
        var textos = new List<Texto>[n];
        var anchos = new float[n];
        for (var i = 0; i < n; i++)
        {
            textos[i] = [new Texto(0, false, Cortes(columnas[i], encabezado, medium: true))];
            for (var f = 0; f < filas.Count; f++)
            {
                var celda = i < filas[f].Length ? filas[f][i] ?? string.Empty : string.Empty;
                textos[i].Add(new Texto(f + 1, EsCorto(celda), Cortes(celda, cuerpo, medium: false)));
            }

            anchos[i] = textos[i].Max(t => Exterior(t.Cortes[^1], relleno));
        }

        if (anchos.Sum() > util)
            return null;
        var sobra = util - anchos.Sum();
        var actual = Medir(textos, anchos, relleno, filas.Count + 1);
        while (sobra > 0.01f)
        {
            (float Ganancia, int Columna, float Paso, (int, int, int, int) Medida)? mejor = null;
            for (var i = 0; i < n; i++)
            {
                var interior = Interior(anchos[i], relleno);
                var siguiente = textos[i].SelectMany(t => t.Cortes).Where(c => c > interior + 0.001f).DefaultIfEmpty(float.MaxValue).Min();
                var paso = Exterior(siguiente, relleno) - anchos[i];
                if (paso > sobra)
                    continue;
                anchos[i] += paso;
                var medida = Medir(textos, anchos, relleno, filas.Count + 1);
                anchos[i] -= paso;
                var ganancia = (((actual.Filas - medida.Filas) * 1000f) + (actual.Celdas - medida.Celdas)) / Math.Max(paso, 0.01f);
                if (ganancia > 0 && (mejor is null || ganancia > mejor.Value.Ganancia))
                    mejor = (ganancia, i, paso, medida);
            }

            if (mejor is not { } m)
                break;
            anchos[m.Columna] += m.Paso;
            sobra -= m.Paso;
            actual = m.Medida;
        }

        // Lo que ya no quita líneas se reparte por igual.
        for (var i = 0; i < n; i++)
            anchos[i] += sobra / n;
        return new Reparto(anchos, cuerpo, relleno, (actual.Cortos, cuerpo < TamanoTexto ? 1 : 0, actual.Filas, actual.Celdas));
    }

    /// <summary>Líneas de las filas (suma de la celda más alta de cada fila), de todas las celdas y textos cortos partidos.</summary>
    private static (int Cortos, int Reducido, int Filas, int Celdas) Medir(List<Texto>[] textos, float[] anchos, float relleno, int filas)
    {
        var alto = new int[filas];
        var celdas = 0;
        var cortos = 0;
        for (var i = 0; i < textos.Length; i++)
        {
            var interior = Interior(anchos[i], relleno);
            foreach (var t in textos[i])
            {
                var lineas = Array.FindIndex(t.Cortes, c => c <= interior + 0.001f) + 1;
                if (lineas == 0)
                    lineas = t.Cortes.Length;
                alto[t.Fila] = Math.Max(alto[t.Fila], lineas);
                celdas += lineas;
                if (t.Corto && lineas > 1)
                    cortos++;
            }
        }

        return (cortos, 0, alto.Sum(), celdas);
    }

    /// <summary>
    /// Ancho interior mínimo de <paramref name="texto"/> para caber en 1, 2, … líneas, hasta las que ocupa con su palabra más ancha
    /// (último elemento). Cada corte se busca por bisección sobre <see cref="Lineas"/>.
    /// </summary>
    private static float[] Cortes(string texto, float tamano, bool medium)
    {
        var palabra = AnchoPalabraMayor(texto, tamano, medium) * Holgura;
        var completo = AnchoTexto(texto, tamano, medium) * Holgura;
        var maximo = Lineas(texto, palabra, tamano, medium);
        var cortes = new float[maximo];
        cortes[0] = completo;
        for (var k = 2; k <= maximo; k++)
        {
            float bajo = palabra, alto = completo;
            for (var paso = 0; paso < 24 && alto - bajo > 0.05f; paso++)
            {
                var medio = (bajo + alto) / 2;
                if (Lineas(texto, medio, tamano, medium) <= k)
                    alto = medio;
                else
                    bajo = medio;
            }

            cortes[k - 1] = Lineas(texto, bajo, tamano, medium) <= k ? bajo : alto;
        }

        for (var k = 1; k < maximo; k++)
            cortes[k] = Math.Min(cortes[k], cortes[k - 1]);
        return cortes;
    }

    /// <summary>Líneas de <paramref name="texto"/> en <paramref name="interior"/> pt partiendo en los espacios (avances con holgura).</summary>
    private static int Lineas(string texto, float interior, float tamano, bool medium)
    {
        var palabras = texto.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (palabras.Length == 0)
            return 1;
        var espacio = AnchoTexto(" ", tamano, medium) * Holgura;
        var lineas = 1;
        var ocupado = -1f;
        foreach (var palabra in palabras)
        {
            var ancho = AnchoTexto(palabra, tamano, medium) * Holgura;
            if (ancho > interior + 0.001f)
                return int.MaxValue;
            if (ocupado < 0)
                ocupado = ancho;
            else if (ocupado + espacio + ancho <= interior + 0.001f)
                ocupado += espacio + ancho;
            else
            {
                lineas++;
                ocupado = ancho;
            }
        }

        return lineas;
    }

    /// <summary>Texto corto que debe quedar en una línea: hasta 3 palabras y 14 caracteres (no vacío).</summary>
    private static bool EsCorto(string texto) =>
        texto.Length is > 0 and <= 14 && texto.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length <= 3;

    /// <summary>
    /// Reparto de último recurso cuando ni con el cuerpo y el relleno menores caben las palabras: cada mínimo (al tamaño de siempre) se
    /// limita a <see cref="TopeMinimo"/> del ancho útil y, si aún no caben, se escalan en proporción.
    /// </summary>
    private static float[] Limitados(IReadOnlyList<string> columnas, IReadOnlyList<string[]> filas, float util, float encabezado)
    {
        var minimos = columnas.Select((columna, i) => Math.Min(util * TopeMinimo, Exterior(Math.Max(
            AnchoPalabraMayor(columna, encabezado, medium: true),
            filas.Where(f => i < f.Length).Select(f => AnchoPalabraMayor(f[i] ?? string.Empty, TamanoTexto)).DefaultIfEmpty(0).Max()) * Holgura, RellenoCelda)))
            .ToArray();
        var escala = Math.Min(1, util / minimos.Sum());
        var sobra = util - (minimos.Sum() * escala);
        return [.. minimos.Select(m => (m * escala) + (sobra / minimos.Length))];
    }

    private static float Exterior(float interior, float relleno) => interior + HolguraFija + (2 * relleno);

    private static float Interior(float exterior, float relleno) => exterior - HolguraFija - (2 * relleno);


    private static char Base(char caracter) => caracter switch
    {
        'á' => 'a', 'é' => 'e', 'í' => 'i', 'ó' => 'o', 'ú' or 'ü' => 'u', 'ñ' => 'n',
        'Á' => 'A', 'É' => 'E', 'Í' => 'I', 'Ó' => 'O', 'Ú' or 'Ü' => 'U', 'Ñ' => 'N',
        _ => caracter,
    };
}
