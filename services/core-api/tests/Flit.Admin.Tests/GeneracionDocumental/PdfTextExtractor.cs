using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;

namespace Flit.Admin.Tests.GeneracionDocumental;

/// <summary>
/// Extractor de texto de un PDF, <b>solo para pruebas</b> (HU #12207, Feature #12201).
///
/// <para><b>Por qué existe.</b> El anexo normativo exige verificación <b>textual</b> y no visual del
/// contenido del PDF (§9.2 y §13.2): «la comprobación se hace extrayendo el texto del PDF […]; una
/// inspección visual del layout no satisface este requisito». Sin extractor, un test solo podría
/// afirmar que el PDF pesa unos kilobytes, que es exactamente la clase de verificación que el anexo
/// descarta. En el repositorio no hay ninguna librería de lectura de PDF y añadir una dependencia
/// externa por un test no se justifica, así que se lee el formato a mano.</para>
///
/// <para><b>Alcance deliberadamente estrecho.</b> Cubre lo que produce QuestPDF/Skia: objetos
/// indirectos sin flujos de objetos comprimidos, contenido con <c>FlateDecode</c>, fuentes con
/// <c>/ToUnicode</c> y cadenas hexadecimales de dos bytes. No pretende ser un lector de PDF general
/// y no debe promoverse a producción.</para>
/// </summary>
internal static class PdfTextExtractor
{
    private static readonly Regex ObjectHeader = new(
        @"(?<num>\d+)\s+(?<gen>\d+)\s+obj",
        RegexOptions.Compiled,
        TimeSpan.FromSeconds(5));

    /// <summary>Texto plano del PDF, con los espacios normalizados a uno.</summary>
    public static string Extract(byte[] pdf)
    {
        ArgumentNullException.ThrowIfNull(pdf);

        var objects = ReadObjects(pdf);
        var text = new StringBuilder();

        foreach (var page in objects.Values.Where(o => o.Dictionary.Contains("/Type /Page")
            || o.Dictionary.Contains("/Type/Page")))
        {
            var fonts = ReadFonts(page, objects);

            foreach (var contentId in References(page.Dictionary, "Contents"))
            {
                if (!objects.TryGetValue(contentId, out var content) || content.Data is null)
                {
                    continue;
                }

                text.Append(ReadContentStream(Encoding.Latin1.GetString(content.Data), fonts));
                text.Append('\n');
            }
        }

        return Regex.Replace(text.ToString(), @"[ \t]+", " ", RegexOptions.None, TimeSpan.FromSeconds(5));
    }

    /// <summary>
    /// Texto sin ningún espacio. <b>Es la forma en la que hay que comparar.</b> Skia dibuja el PDF
    /// glifo a glifo, posicionando cada uno con su propio operador, de modo que el flujo de
    /// contenido no conserva las palabras: pedirle al extractor que reconstruya la separación
    /// original sería adivinarla. Aplanar ambos lados de la comparación evita esa adivinanza sin
    /// perder poder de detección —«firma electrónica» sigue encontrándose—.
    /// </summary>
    public static string Flatten(string text) =>
        Regex.Replace(text, @"\s+", string.Empty, RegexOptions.None, TimeSpan.FromSeconds(5));

    /// <summary>¿Aparece el literal en el texto extraído, ignorando espacios y mayúsculas?</summary>
    public static bool Contains(string extracted, string literal) =>
        Flatten(extracted).Contains(Flatten(literal), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// ¿Aparece el literal <b>respetando mayúsculas</b>? (HU #12208).
    ///
    /// <para>Existe por el escenario B. Lo que §9.2 prohíbe es el <b>rótulo</b> «ADQUIRENTE» o
    /// «LOCATARIO» en el bloque de firmas; la palabra «locatario» en minúscula sí aparece —y debe
    /// aparecer— en las cláusulas declarativas y en la propia nota de firmas del anexo, que dice
    /// «el locatario no firma el Formato Único». Una comparación insensible a mayúsculas haría
    /// imposible cumplir a la vez §8.2 y §13.2.</para>
    /// </summary>
    public static bool ContainsLiteral(string extracted, string literal) =>
        Flatten(extracted).Contains(Flatten(literal), StringComparison.Ordinal);

    /// <summary>
    /// Texto del <b>bloque de firmas</b>: desde el encabezado «En …, a los N días del mes de …»
    /// hasta el final del documento (anexo §9.1 a §9.3). Devuelto aplanado.
    ///
    /// <para>La verificación de §13.2 está <b>acotada al bloque de firmas</b>: el nombre del
    /// locatario sí puede aparecer en la cláusula segunda. Recortar aquí es lo que hace que el test
    /// compruebe lo que el anexo pide y no otra cosa más laxa o más estricta.</para>
    /// </summary>
    public static string SignatureBlock(string extracted)
    {
        var plano = Flatten(extracted);
        var marca = Flatten("días del mes de");
        var at = plano.IndexOf(marca, StringComparison.Ordinal);

        if (at < 0)
        {
            throw new InvalidOperationException(
                "No se encontró el encabezado del bloque de firmas en el texto extraído: sin él, "
                + "cualquier aserción sobre «el bloque de firmas» sería sobre el documento entero.");
        }

        return plano[at..];
    }

    private sealed record PdfObject(int Number, string Dictionary, byte[]? Data);

    private static Dictionary<int, PdfObject> ReadObjects(byte[] pdf)
    {
        var raw = Encoding.Latin1.GetString(pdf);
        var objects = new Dictionary<int, PdfObject>();

        foreach (Match header in ObjectHeader.Matches(raw))
        {
            var number = int.Parse(header.Groups["num"].Value, CultureInfo.InvariantCulture);
            var bodyStart = header.Index + header.Length;
            var endObj = raw.IndexOf("endobj", bodyStart, StringComparison.Ordinal);
            if (endObj < 0)
            {
                continue;
            }

            var body = raw[bodyStart..endObj];
            var streamAt = body.IndexOf("stream", StringComparison.Ordinal);
            byte[]? data = null;
            var dictionary = body;

            if (streamAt >= 0)
            {
                dictionary = body[..streamAt];

                // Tras la palabra clave viene CRLF o LF antes del primer byte del flujo.
                var dataStart = bodyStart + streamAt + "stream".Length;
                if (raw[dataStart] == '\r')
                {
                    dataStart++;
                }

                if (raw[dataStart] == '\n')
                {
                    dataStart++;
                }

                var dataEnd = raw.IndexOf("endstream", dataStart, StringComparison.Ordinal);
                if (dataEnd > dataStart)
                {
                    var slice = pdf[dataStart..dataEnd];
                    data = dictionary.Contains("FlateDecode", StringComparison.Ordinal)
                        ? Inflate(slice)
                        : slice;
                }
            }

            objects[number] = new PdfObject(number, dictionary, data);
        }

        return objects;
    }

    private static byte[]? Inflate(byte[] compressed)
    {
        try
        {
            using var input = new MemoryStream(compressed);
            using var zlib = new ZLibStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();
            zlib.CopyTo(output);
            return output.ToArray();
        }
        catch (InvalidDataException)
        {
            // Un flujo que no es zlib (imagen, fuente incrustada) no aporta texto: se ignora.
            return null;
        }
    }

    /// <summary>Nombre de fuente del recurso (<c>/F1</c>) → tabla de códigos a Unicode.</summary>
    private static Dictionary<string, Dictionary<int, string>> ReadFonts(
        PdfObject page,
        Dictionary<int, PdfObject> objects)
    {
        var fonts = new Dictionary<string, Dictionary<int, string>>(StringComparer.Ordinal);

        var resources = page.Dictionary;
        foreach (var resourceId in References(page.Dictionary, "Resources"))
        {
            if (objects.TryGetValue(resourceId, out var resourceObject))
            {
                resources = resourceObject.Dictionary;
            }
        }

        var fontSection = Section(resources, "/Font");
        foreach (Match entry in Regex.Matches(
            fontSection,
            @"/(?<name>[A-Za-z0-9#+.-]+)\s+(?<id>\d+)\s+\d+\s+R",
            RegexOptions.None,
            TimeSpan.FromSeconds(5)))
        {
            var id = int.Parse(entry.Groups["id"].Value, CultureInfo.InvariantCulture);
            if (!objects.TryGetValue(id, out var font))
            {
                continue;
            }

            foreach (var toUnicodeId in References(font.Dictionary, "ToUnicode"))
            {
                if (objects.TryGetValue(toUnicodeId, out var cmap) && cmap.Data is not null)
                {
                    fonts[entry.Groups["name"].Value] = ParseCMap(Encoding.Latin1.GetString(cmap.Data));
                }
            }
        }

        return fonts;
    }

    /// <summary>Tabla <c>/ToUnicode</c>: pares <c>beginbfchar</c> y rangos <c>beginbfrange</c>.</summary>
    private static Dictionary<int, string> ParseCMap(string cmap)
    {
        var map = new Dictionary<int, string>();

        foreach (Match block in Regex.Matches(
            cmap, @"beginbfchar(?<body>.*?)endbfchar",
            RegexOptions.Singleline, TimeSpan.FromSeconds(5)))
        {
            foreach (Match pair in Regex.Matches(
                block.Groups["body"].Value, @"<(?<src>[0-9A-Fa-f]+)>\s*<(?<dst>[0-9A-Fa-f]+)>",
                RegexOptions.None, TimeSpan.FromSeconds(5)))
            {
                map[Convert.ToInt32(pair.Groups["src"].Value, 16)] = FromUtf16Hex(pair.Groups["dst"].Value);
            }
        }

        foreach (Match block in Regex.Matches(
            cmap, @"beginbfrange(?<body>.*?)endbfrange",
            RegexOptions.Singleline, TimeSpan.FromSeconds(5)))
        {
            foreach (Match range in Regex.Matches(
                block.Groups["body"].Value,
                @"<(?<lo>[0-9A-Fa-f]+)>\s*<(?<hi>[0-9A-Fa-f]+)>\s*<(?<dst>[0-9A-Fa-f]+)>",
                RegexOptions.None, TimeSpan.FromSeconds(5)))
            {
                var lo = Convert.ToInt32(range.Groups["lo"].Value, 16);
                var hi = Convert.ToInt32(range.Groups["hi"].Value, 16);
                var dst = Convert.ToInt32(range.Groups["dst"].Value, 16);

                for (var code = lo; code <= hi && code - lo < 512; code++)
                {
                    map[code] = char.ConvertFromUtf32(dst + (code - lo));
                }
            }
        }

        return map;
    }

    private static string FromUtf16Hex(string hex)
    {
        var sb = new StringBuilder();
        for (var i = 0; i + 4 <= hex.Length; i += 4)
        {
            sb.Append((char)Convert.ToInt32(hex.Substring(i, 4), 16));
        }

        return sb.ToString();
    }

    /// <summary>
    /// Recorre el flujo de contenido y va traduciendo las cadenas mostradas con la tabla de la
    /// fuente activa (<c>Tf</c>). Solo se atiende a lo necesario: <c>Tj</c>, <c>TJ</c> y los saltos
    /// de línea de texto.
    /// </summary>
    private static string ReadContentStream(string content, Dictionary<string, Dictionary<int, string>> fonts)
    {
        var text = new StringBuilder();
        Dictionary<int, string>? current = null;

        foreach (Match token in Regex.Matches(
            content,
            @"/(?<font>[A-Za-z0-9#+.-]+)\s+[\d.]+\s+Tf|<(?<hex>[0-9A-Fa-f\s]*)>\s*Tj|\((?<lit>(?:\\.|[^)])*)\)\s*Tj|(?<nl>T\*|Td|TD|ET)",
            RegexOptions.None,
            TimeSpan.FromSeconds(10)))
        {
            if (token.Groups["font"].Success)
            {
                fonts.TryGetValue(token.Groups["font"].Value, out current);
            }
            else if (token.Groups["hex"].Success)
            {
                text.Append(DecodeHex(token.Groups["hex"].Value, current));
            }
            else if (token.Groups["lit"].Success)
            {
                text.Append(token.Groups["lit"].Value);
            }
            else if (token.Groups["nl"].Success)
            {
                // Solo los saltos de línea de texto separan; Td posiciona cada glifo suelto y
                // tratarlo como separador partiría todas las palabras del documento.
                text.Append(' ');
            }
        }

        return text.ToString();
    }

    private static string DecodeHex(string hex, Dictionary<int, string>? cmap)
    {
        var clean = Regex.Replace(hex, @"\s", string.Empty, RegexOptions.None, TimeSpan.FromSeconds(5));
        var sb = new StringBuilder();

        for (var i = 0; i + 4 <= clean.Length; i += 4)
        {
            var code = Convert.ToInt32(clean.Substring(i, 4), 16);
            sb.Append(cmap is not null && cmap.TryGetValue(code, out var value)
                ? value
                : string.Empty);
        }

        return sb.ToString();
    }

    /// <summary>Sub-diccionario equilibrando <c>&lt;&lt;</c> y <c>&gt;&gt;</c> a partir de una clave.</summary>
    private static string Section(string dictionary, string key)
    {
        var at = dictionary.IndexOf(key, StringComparison.Ordinal);
        if (at < 0)
        {
            return string.Empty;
        }

        var open = dictionary.IndexOf("<<", at, StringComparison.Ordinal);
        if (open < 0)
        {
            return string.Empty;
        }

        var depth = 0;
        for (var i = open; i < dictionary.Length - 1; i++)
        {
            if (dictionary[i] == '<' && dictionary[i + 1] == '<')
            {
                depth++;
                i++;
            }
            else if (dictionary[i] == '>' && dictionary[i + 1] == '>')
            {
                depth--;
                i++;

                if (depth == 0)
                {
                    return dictionary[open..(i + 1)];
                }
            }
        }

        return dictionary[open..];
    }

    private static IEnumerable<int> References(string dictionary, string key)
    {
        foreach (Match match in Regex.Matches(
            dictionary,
            $@"/{key}\s+(?:\[(?<array>[^\]]*)\]|(?<id>\d+)\s+\d+\s+R)",
            RegexOptions.None,
            TimeSpan.FromSeconds(5)))
        {
            if (match.Groups["id"].Success)
            {
                yield return int.Parse(match.Groups["id"].Value, CultureInfo.InvariantCulture);
                continue;
            }

            foreach (Match item in Regex.Matches(
                match.Groups["array"].Value, @"(?<id>\d+)\s+\d+\s+R",
                RegexOptions.None, TimeSpan.FromSeconds(5)))
            {
                yield return int.Parse(item.Groups["id"].Value, CultureInfo.InvariantCulture);
            }
        }
    }
}
