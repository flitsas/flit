using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace Flit.Admin.Tests.Architecture;

/// <summary>
/// HU #12662 (AC2 y AC4) — guardián del huso de negocio sobre el código fuente de core-api.
///
/// <para>
/// Dos reglas que ningún test de comportamiento puede sostener por sí solo: que el desfase de
/// Colombia se declare UNA vez, y que nadie reintroduzca la búsqueda del huso en la base del
/// sistema. Lo segundo importa porque los proyectos compilan con
/// <c>InvariantGlobalization=true</c>: en Linux con ICU la búsqueda resuelve y el fallo no
/// aparecería hasta publicar en AOT.
/// </para>
/// </summary>
public class ColombiaTimeArchitectureTests
{
    /// <summary>Archivo que sí puede declarar el desfase: es la fuente de verdad.</summary>
    private const string Canonico = "Flit.Queries.Domain/Time/ColombiaTime.cs";

    private static readonly Regex DesfaseLiteral = new(
        @"TimeSpan\.FromHours\(\s*-\s*5\s*\)", RegexOptions.Compiled);

    private static readonly Regex BusquedaDeHuso = new(
        "FindSystemTimeZoneById", RegexOptions.Compiled);

    private static readonly Regex BloqueDeComentario = new(
        @"/\*.*?\*/", RegexOptions.Compiled | RegexOptions.Singleline);

    [Fact]
    public void ElDesfaseDeColombiaSeDeclaraUnaSolaVez()
    {
        var infractores = Fuentes()
            .Where(f => f.Rel != Canonico && DesfaseLiteral.IsMatch(f.Codigo))
            .Select(f => f.Rel)
            .ToArray();

        infractores.Should().BeEmpty(
            "el desfase vive en ColombiaTime.Offset; declararlo aparte permite que dos sitios " +
            "discrepen sin que nada lo note");
    }

    [Fact]
    public void NadieBuscaElHusoEnLaBaseDeHusosDelSistema()
    {
        var infractores = Fuentes()
            .Where(f => BusquedaDeHuso.IsMatch(f.Codigo))
            .Select(f => f.Rel)
            .ToArray();

        infractores.Should().BeEmpty(
            "con InvariantGlobalization el id IANA «America/Bogota» no existe y lanza " +
            "TimeZoneNotFoundException; el huso se construye con offset fijo en ColombiaTime.Zone");
    }

    private static IEnumerable<(string Rel, string Codigo)> Fuentes()
    {
        var src = DirectorioDeFuentes();
        var sep = Path.DirectorySeparatorChar;

        foreach (var file in Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{sep}obj{sep}", StringComparison.Ordinal)
                || file.Contains($"{sep}bin{sep}", StringComparison.Ordinal))
                continue;

            yield return (
                Path.GetRelativePath(src, file).Replace('\\', '/'),
                SinComentarios(File.ReadAllText(file)));
        }
    }

    /// <summary>
    /// Quita los comentarios antes de buscar: ambas reglas hablan de CÓDIGO, y la documentación de
    /// <c>ColombiaTime</c> nombra <c>FindSystemTimeZoneById</c> justamente para explicar por qué no
    /// se usa. Se retiran las líneas que empiezan por <c>//</c> y los bloques delimitados. Un
    /// comentario al final de una línea con código se deja estar: recortarlo obligaría a distinguir
    /// cadenas de texto y no aporta a lo que aquí se vigila.
    /// </summary>
    private static string SinComentarios(string fuente)
    {
        var sinBloques = BloqueDeComentario.Replace(fuente, string.Empty);

        return string.Join('\n', sinBloques
            .Split('\n')
            .Where(l => !l.TrimStart().StartsWith("//", StringComparison.Ordinal)));
    }

    /// <summary>
    /// Se ancla en la ruta de ESTE archivo en tiempo de compilación, no en
    /// <c>AppContext.BaseDirectory</c>: así el test sigue encontrando el fuente aunque la
    /// compilación mande la salida fuera del repositorio (algo habitual en local, cuando el
    /// stack está corriendo y bloquea los binarios).
    /// </summary>
    private static string DirectorioDeFuentes([CallerFilePath] string esteArchivo = "")
    {
        // .../services/core-api/tests/Flit.Admin.Tests/Architecture/<este archivo>
        var dir = Path.GetFullPath(Path.Combine(
            Path.GetDirectoryName(esteArchivo)!, "..", "..", "..", "src"));

        Directory.Exists(dir).Should().BeTrue($"no se encontró el directorio de fuentes en {dir}");
        return dir;
    }
}
