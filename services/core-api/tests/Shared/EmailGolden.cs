using System.Text;
using Flit.Modules.Security.Domain.Auth;
using FluentAssertions;

namespace Flit.Tests.Shared;

/// <summary>
/// HU #11350 — comparación de un <see cref="EmailMessage"/> contra sus golden files de asunto y
/// cuerpo. Archivo ENLAZADO (no copiado) desde los proyectos de prueba que lo usan, para que la
/// regla de comparación no se bifurque entre módulos.
/// <para>
/// <b>Saltos de línea:</b> se normalizan a <c>\n</c> en AMBOS lados. El repo no tiene
/// <c>.gitattributes</c> y <c>core.autocrlf=true</c>, así que en Windows tanto el
/// <c>.golden.txt</c> como los literales <c>"""…"""</c> de C# salen del checkout con CRLF,
/// mientras que en el contenedor Linux salen con LF. Normalizar solo el lado producido —el error
/// que ya cuesta 9 fallos conocidos en esta suite— haría que el golden fallara en una máquina y
/// pasara en la otra.
/// </para>
/// </summary>
internal static class EmailGolden
{
    private const string UpdateVariable = "GOLDEN_UPDATE";

    /// <summary>
    /// Congela asunto y cuerpo del mensaje. Los dos se comparan por separado para que el fallo
    /// diga cuál de los dos derivó (AC2).
    /// </summary>
    internal static void Assert(EmailMessage message, string plantilla)
    {
        AssertPart(message.Subject, plantilla, "subject", "el ASUNTO");
        AssertPart(message.HtmlBody, plantilla, "body", "el CUERPO");
    }

    private static void AssertPart(string producido, string plantilla, string parte, string etiqueta)
    {
        var nombre = $"{plantilla}.{parte}.golden.txt";
        var normalizado = Lf(producido);

        if (Environment.GetEnvironmentVariable(UpdateVariable) == "1")
        {
            Write(Path.Combine(OutputFixtures(), nombre), normalizado);
            // También al árbol de fuentes: BaseDirectory es bin/ y de ahí no se commitea nada.
            Write(Path.Combine(SourceFixtures(), nombre), normalizado);
            return;
        }

        var ruta = Path.Combine(OutputFixtures(), nombre);
        File.Exists(ruta).Should().BeTrue(
            $"el golden '{nombre}' debe existir; genéralo una sola vez con {UpdateVariable}=1");

        normalizado.Should().Be(
            Lf(File.ReadAllText(ruta)),
            "cambió {0} de la plantilla '{1}'. Si el cambio es deliberado, regenera el golden con "
            + "{2}=1 en un commit aparte que explique por qué; si no lo es, el cambio está mal",
            etiqueta,
            plantilla,
            UpdateVariable);
    }

    private static string Lf(string valor) => valor.Replace("\r\n", "\n", StringComparison.Ordinal);

    private static void Write(string ruta, string contenido)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ruta)!);
        // UTF8 sin BOM y LF explícito: el archivo debe ser byte a byte el mismo en Windows y Linux.
        File.WriteAllText(ruta, contenido, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private static string OutputFixtures() => Path.Combine(AppContext.BaseDirectory, "Fixtures", "Email");

    private static string SourceFixtures()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && Directory.GetFiles(dir.FullName, "*.csproj").Length == 0)
        {
            dir = dir.Parent;
        }

        if (dir is null)
        {
            throw new InvalidOperationException(
                "No se encontró el .csproj del proyecto de pruebas subiendo desde AppContext.BaseDirectory; "
                + $"sin él no se puede escribir el golden al árbol de fuentes con {UpdateVariable}=1.");
        }

        return Path.Combine(dir.FullName, "Fixtures", "Email");
    }
}
