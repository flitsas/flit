using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using QuestPDF.Drawing;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Flit.Comparendos.Api.Infraestructura.Pdf;

/// <summary>
/// Convierte a PDF el HTML acotado que produce el editor de plantillas (no hay un motor HTML→PDF en el repo; core-api usa
/// QuestPDF). Admite: h1-h3, p, div, br, b/strong, i/em, u, ul/ol/li, table/tr/th/td, hr, img con data URI (la firma) y
/// <c>style="text-align:…"</c> en los bloques. Lo demás se ignora y su texto se conserva. Letra Poppins, carta, márgenes
/// de oficio y numeración de páginas.
/// </summary>
public static partial class HtmlAPdf
{
    private static int _fuentes;

    /// <param name="titulo">Título del PDF: el navegador lo muestra en la pestaña en vez de un identificador (hallazgo B9-4).</param>
    public static byte[] Generar(string html, string? marcaDeAgua = null, string? titulo = null)
    {
        Inicializar();
        var bloques = Parsear(html ?? string.Empty);
        return Document.Create(doc => doc.Page(page =>
        {
            page.Size(PageSizes.Letter);
            page.Margin(2.2f, Unit.Centimetre);
            page.DefaultTextStyle(t => t.FontFamily("Poppins").FontSize(10.5f).LineHeight(1.35f).FontColor("#1F2A3D"));
            if (marcaDeAgua is { Length: > 0 })
            {
                page.Foreground().AlignCenter().AlignMiddle().Rotate(-30)
                    .Text(marcaDeAgua).FontSize(64).Bold().FontColor("#E6EAF0");
            }

            page.Content().Column(col =>
            {
                col.Spacing(6);
                foreach (var b in bloques)
                    Dibujar(col.Item(), b);
            });
            page.Footer().AlignRight().Text(t =>
            {
                t.DefaultTextStyle(s => s.FontSize(8).FontColor("#8792A6"));
                t.CurrentPageNumber();
                t.Span(" / ");
                t.TotalPages();
            });
        })).WithMetadata(new DocumentMetadata { Title = titulo ?? string.Empty, Author = "FLIT · Gestión de Comparendos" }).GeneratePdf();
    }

    private static void Inicializar()
    {
        if (Interlocked.Exchange(ref _fuentes, 1) != 0)
            return;
        QuestPDF.Settings.License = LicenseType.Community;
        var asm = typeof(HtmlAPdf).Assembly;
        foreach (var nombre in asm.GetManifestResourceNames().Where(n => n.EndsWith(".ttf", StringComparison.Ordinal)))
        {
            using var s = asm.GetManifestResourceStream(nombre)!;
            FontManager.RegisterFont(s);
        }
    }

    // ── Modelo ─────────────────────────────────────────────────────────────────────────────────────────────────────────
    private sealed record Tramo(string Texto, bool Negrita, bool Cursiva, bool Subrayado, bool Salto = false);

    private abstract record Bloque(string Alineacion);

    private sealed record Parrafo(IReadOnlyList<Tramo> Tramos, string Alineacion, float Tamano, bool Titulo) : Bloque(Alineacion);

    private sealed record Lista(IReadOnlyList<IReadOnlyList<Tramo>> Items, bool Numerada) : Bloque("left");

    private sealed record Tabla(IReadOnlyList<IReadOnlyList<(IReadOnlyList<Tramo> Tramos, bool Encabezado)>> Filas) : Bloque("left");

    private sealed record Imagen(byte[] Bytes, float Ancho, string Alin) : Bloque(Alin);

    private sealed record Linea() : Bloque("left");

    // ── Parser tolerante del subconjunto ───────────────────────────────────────────────────────────────────────────────
    [GeneratedRegex(@"<(/?)([a-zA-Z0-9]+)([^>]*)>|([^<]+)", RegexOptions.CultureInvariant)]
    private static partial Regex TokenRegex();

    [GeneratedRegex(@"text-align\s*:\s*(left|center|right|justify)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AlineacionRegex();

    [GeneratedRegex(@"src\s*=\s*""data:image/(png|jpeg|jpg);base64,([^""]+)""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ImagenRegex();

    [GeneratedRegex(@"width\s*=\s*""?(\d+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AnchoRegex();

    private static List<Bloque> Parsear(string html)
    {
        var bloques = new List<Bloque>();
        var tramos = new List<Tramo>();
        var negrita = 0;
        var cursiva = 0;
        var subrayado = 0;
        var alineacion = "left";
        var tamano = 10.5f;
        var titulo = false;
        List<IReadOnlyList<Tramo>>? lista = null;
        var numerada = false;
        List<IReadOnlyList<(IReadOnlyList<Tramo>, bool)>>? tabla = null;
        List<(IReadOnlyList<Tramo>, bool)>? fila = null;
        var celdaEncabezado = false;

        void CerrarParrafo()
        {
            var limpios = Limpiar(tramos);
            if (limpios.Count > 0)
            {
                if (fila is not null)
                    fila.Add((limpios, celdaEncabezado));
                else if (lista is not null)
                    lista.Add(limpios);
                else
                    bloques.Add(new Parrafo(limpios, alineacion, tamano, titulo));
            }

            tramos = [];
        }

        foreach (Match m in TokenRegex().Matches(html))
        {
            if (m.Groups[4].Success)
            {
                var texto = WebUtility.HtmlDecode(Regex.Replace(m.Groups[4].Value, @"\s+", " "));
                if (texto.Length > 0)
                    tramos.Add(new Tramo(texto, negrita > 0 || titulo, cursiva > 0, subrayado > 0));
                continue;
            }

            var cierre = m.Groups[1].Value == "/";
            var tag = m.Groups[2].Value.ToLowerInvariant();
            var atributos = m.Groups[3].Value;
            switch (tag)
            {
                case "b" or "strong": negrita += cierre ? -1 : 1; break;
                case "i" or "em": cursiva += cierre ? -1 : 1; break;
                case "u": subrayado += cierre ? -1 : 1; break;
                case "br": tramos.Add(new Tramo("\n", false, false, false, Salto: true)); break;
                case "p" or "div" or "h1" or "h2" or "h3":
                    CerrarParrafo();
                    if (!cierre)
                    {
                        alineacion = AlineacionRegex().Match(atributos) is { Success: true } a ? a.Groups[1].Value.ToLowerInvariant() : "left";
                        (tamano, titulo) = tag switch { "h1" => (16f, true), "h2" => (13.5f, true), "h3" => (11.5f, true), _ => (10.5f, false) };
                    }
                    else
                    {
                        (alineacion, tamano, titulo) = ("left", 10.5f, false);
                    }

                    break;
                case "ul" or "ol":
                    CerrarParrafo();
                    if (!cierre)
                    {
                        lista = [];
                        numerada = tag == "ol";
                    }
                    else if (lista is not null)
                    {
                        bloques.Add(new Lista(lista, numerada));
                        lista = null;
                    }

                    break;
                case "li": CerrarParrafo(); break;
                case "table":
                    CerrarParrafo();
                    if (!cierre)
                    {
                        tabla = [];
                    }
                    else if (tabla is not null)
                    {
                        bloques.Add(new Tabla(tabla));
                        tabla = null;
                    }

                    break;
                case "tr":
                    if (!cierre)
                    {
                        fila = [];
                    }
                    else if (fila is not null)
                    {
                        CerrarParrafo();
                        tabla?.Add(fila);
                        fila = null;
                    }

                    break;
                case "td" or "th":
                    if (cierre)
                    {
                        var antes = fila?.Count ?? 0;
                        CerrarParrafo();
                        if (fila is not null && fila.Count == antes)
                            fila.Add(([], celdaEncabezado));
                    }
                    else
                    {
                        celdaEncabezado = tag == "th";
                    }

                    break;
                case "hr":
                    CerrarParrafo();
                    bloques.Add(new Linea());
                    break;
                case "img":
                    if (ImagenRegex().Match(atributos) is { Success: true } img)
                    {
                        CerrarParrafo();
                        var ancho = AnchoRegex().Match(atributos) is { Success: true } w ? float.Parse(w.Groups[1].Value, CultureInfo.InvariantCulture) : 160f;
                        bloques.Add(new Imagen(Convert.FromBase64String(img.Groups[2].Value), Math.Clamp(ancho, 40f, 480f), alineacion));
                    }

                    break;
            }
        }

        CerrarParrafo();
        return bloques;
    }

    private static List<Tramo> Limpiar(List<Tramo> tramos)
    {
        // Quita espacios al principio y al final del bloque.
        var lista = tramos.ToList();
        while (lista.Count > 0 && !lista[0].Salto && string.IsNullOrWhiteSpace(lista[0].Texto))
            lista.RemoveAt(0);
        while (lista.Count > 0 && !lista[^1].Salto && string.IsNullOrWhiteSpace(lista[^1].Texto))
            lista.RemoveAt(lista.Count - 1);
        if (lista.Count > 0 && !lista[0].Salto)
            lista[0] = lista[0] with { Texto = lista[0].Texto.TrimStart() };
        if (lista.Count > 0 && !lista[^1].Salto)
            lista[^1] = lista[^1] with { Texto = lista[^1].Texto.TrimEnd() };
        return lista.Any(t => t.Texto.Length > 0) ? lista : [];
    }

    // ── Dibujo ─────────────────────────────────────────────────────────────────────────────────────────────────────────
    private static void Dibujar(IContainer c, Bloque b)
    {
        switch (b)
        {
            case Parrafo p:
                Alinear(c.PaddingTop(p.Titulo ? 6 : 0), p.Alineacion).Text(t => Texto(t, p.Tramos, p.Tamano, p.Alineacion == "justify"));
                break;
            case Lista l:
                c.Column(col =>
                {
                    for (var i = 0; i < l.Items.Count; i++)
                    {
                        var item = l.Items[i];
                        var marca = l.Numerada ? $"{i + 1}." : "•";
                        col.Item().Row(r =>
                        {
                            r.ConstantItem(18).Text(marca);
                            r.RelativeItem().Text(t => Texto(t, item, 10.5f, false));
                        });
                    }
                });
                break;
            case Tabla tb:
                var columnas = tb.Filas.Count == 0 ? 0 : tb.Filas.Max(f => f.Count);
                if (columnas == 0)
                    break;
                c.Table(t =>
                {
                    t.ColumnsDefinition(cd =>
                    {
                        for (var i = 0; i < columnas; i++)
                            cd.RelativeColumn();
                    });
                    foreach (var fila in tb.Filas)
                    {
                        for (var i = 0; i < columnas; i++)
                        {
                            var (tramos, encabezado) = i < fila.Count ? fila[i] : ([], false);
                            var celda = t.Cell().Border(0.5f).BorderColor("#C9D2DE").Padding(4);
                            if (encabezado)
                                celda = celda.Background("#EEF2F7");
                            celda.Text(x => Texto(x, encabezado ? tramos.Select(r => r with { Negrita = true }).ToList() : tramos, 9.5f, false));
                        }
                    }
                });
                break;
            case Imagen img:
                Alinear(c, img.Alineacion).Width(img.Ancho * 0.75f).Image(img.Bytes).FitWidth();
                break;
            case Linea:
                c.PaddingVertical(4).LineHorizontal(0.6f).LineColor("#C9D2DE");
                break;
        }
    }

    private static IContainer Alinear(IContainer c, string alineacion) => alineacion switch
    {
        "center" => c.AlignCenter(),
        "right" => c.AlignRight(),
        _ => c.AlignLeft(),
    };

    private static void Texto(TextDescriptor t, IReadOnlyList<Tramo> tramos, float tamano, bool justificar)
    {
        if (justificar)
            t.Justify();
        foreach (var tr in tramos)
        {
            var s = t.Span(tr.Texto).FontSize(tamano);
            if (tr.Negrita)
                s.SemiBold();
            if (tr.Cursiva)
                s.Italic();
            if (tr.Subrayado)
                s.Underline();
        }
    }

    /// <summary>Texto plano (para vista previa sin PDF y pruebas): el contenido legible del HTML acotado.</summary>
    public static string TextoPlano(string html)
    {
        var sb = new StringBuilder();
        foreach (var b in Parsear(html ?? string.Empty))
        {
            switch (b)
            {
                case Parrafo p: sb.AppendLine(string.Concat(p.Tramos.Select(t => t.Texto))); break;
                case Lista l: foreach (var i in l.Items) sb.Append("- ").AppendLine(string.Concat(i.Select(t => t.Texto))); break;
                case Tabla tb: foreach (var f in tb.Filas) sb.AppendLine(string.Join(" | ", f.Select(c => string.Concat(c.Tramos.Select(t => t.Texto))))); break;
                case Imagen: sb.AppendLine("[imagen]"); break;
            }
        }

        return sb.ToString();
    }
}
