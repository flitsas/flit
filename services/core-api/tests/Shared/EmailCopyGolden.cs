using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using FluentAssertions;

namespace Flit.Tests.Shared;

/// <summary>
/// Guardia de copy del rediseño «Correos 2.0»: congela el TEXTO VISIBLE de un correo (asunto +
/// nodos de texto del cuerpo), no su marcado. Un cambio de diseño (tablas, colores, tarjeta,
/// botón) deja este golden intacto; un cambio de una sola palabra lo rompe.
/// <para>
/// Extracción: se descartan <c>&lt;head&gt;</c>, <c>&lt;style&gt;</c> y comentarios (incluidos
/// los condicionales de Outlook); las etiquetas en línea (<c>strong</c>, <c>a</c>, <c>span</c>…) no
/// separan texto y cada etiqueta de bloque (<c>p</c>, <c>td</c>, <c>br</c>…) separa nodos; las entidades se decodifican y los
/// espacios se colapsan. El archivo guarda un nodo por línea para que el diff se lea, pero la
/// comparación colapsa todo el espacio en blanco: partir un párrafo en dos celdas no es un
/// cambio de copy, cambiar su texto sí. Los atributos (<c>alt</c>, <c>href</c>) no son texto
/// visible y no entran.
/// </para>
/// </summary>
internal static class EmailCopyGolden
{
    private const string UpdateVariable = "GOLDEN_UPDATE";

    private static readonly Regex Head = new("<head[^>]*>.*?</head>", RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex Style = new("<style[^>]*>.*?</style>", RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex Comment = new("<!--.*?-->", RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex InlineTag = new(@"</?(a|b|strong|span|em|i|u|font|small|sup|sub)(\s[^>]*)?/?>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex Tag = new("<[^>]+>", RegexOptions.Compiled);
    private static readonly Regex Spaces = new(@"\s+", RegexOptions.Compiled);

    /// <summary>Texto visible del correo, un nodo por línea, con el asunto en la primera.</summary>
    internal static string Extract(string subject, string html)
    {
        var body = InlineTag.Replace(Comment.Replace(Style.Replace(Head.Replace(html, " "), " "), " "), string.Empty);
        var nodes = Tag.Split(body)
            .Select(n => Spaces.Replace(WebUtility.HtmlDecode(n), " ").Trim())
            .Where(n => n.Length > 0);
        var sb = new StringBuilder();
        sb.Append("Asunto: ").Append(subject.Trim()).Append('\n');
        foreach (var node in nodes)
            sb.Append(node).Append('\n');
        return sb.ToString();
    }

    /// <summary>Texto visible del cuerpo en una sola línea (para aserciones de contenido sin acoplarse al marcado).</summary>
    internal static string VisibleText(string html) => Collapse(Extract(string.Empty, html)["Asunto:".Length..]);

    internal static void Assert(string subject, string html, string plantilla)
    {
        var nombre = $"{plantilla}.text.golden.txt";
        var producido = Extract(subject, html);

        if (Environment.GetEnvironmentVariable(UpdateVariable) == "1")
        {
            Write(Path.Combine(OutputFixtures(), nombre), producido);
            Write(Path.Combine(SourceFixtures(), nombre), producido);
            return;
        }

        var ruta = Path.Combine(OutputFixtures(), nombre);
        File.Exists(ruta).Should().BeTrue(
            $"el golden de copy '{nombre}' debe existir; genéralo una sola vez con {UpdateVariable}=1");

        Collapse(producido).Should().Be(
            Collapse(File.ReadAllText(ruta)),
            "cambió el TEXTO visible de '{0}'. El rediseño solo puede cambiar el marcado: el copy está congelado",
            plantilla);
    }

    private static string Collapse(string value) => Spaces.Replace(value, " ").Trim();

    private static void Write(string ruta, string contenido)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ruta)!);
        File.WriteAllText(ruta, contenido, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private static string OutputFixtures() => Path.Combine(AppContext.BaseDirectory, "Fixtures", "Email");

    private static string SourceFixtures()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && Directory.GetFiles(dir.FullName, "*.csproj").Length == 0)
            dir = dir.Parent;
        return dir is null
            ? throw new InvalidOperationException("No se encontró el .csproj del proyecto de pruebas.")
            : Path.Combine(dir.FullName, "Fixtures", "Email");
    }
}
