using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace Flit.Admin.Tests.Architecture;

/// <summary>
/// HU #13076 AC4 — el ámbito de lectura entre compañías del feed externo (<c>ExternalSyncReadScope</c>)
/// solo lo usa el repositorio de sincronización. Ningún otro componente puede abrir esa lectura: el
/// ámbito no es un permiso de RLS (el rol de core-api ya lee todas las compañías), así que la
/// exclusividad la sostiene esta prueba sobre el código fuente.
/// </summary>
public class ExternalSyncScopeArchitectureTests
{
    private const string Ambito = "Flit.Infrastructure/Persistence/ExternalSync/ExternalSyncReadScope.cs";
    private const string Repositorio = "Flit.Infrastructure/Persistence/Repositories/ProcedureSyncReadRepository.cs";

    private static readonly Regex UsoDelAmbito = new(@"\bExternalSyncReadScope\b", RegexOptions.Compiled);

    private static readonly Regex BloqueDeComentario = new(@"/\*.*?\*/", RegexOptions.Compiled | RegexOptions.Singleline);

    [Fact]
    public void AC4_SoloElRepositorioDeSincronizacionUsaElAmbito()
    {
        var usos = Fuentes()
            .Where(f => UsoDelAmbito.IsMatch(f.Codigo))
            .Select(f => f.Rel)
            .ToArray();

        usos.Should().BeEquivalentTo(
            [Ambito, Repositorio],
            "la lectura entre compañías del feed solo puede abrirla ProcedureSyncReadRepository");
    }

    [Fact]
    public void AC4_ElAmbitoNoEsVisibleFueraDeInfrastructure()
    {
        var tipo = Assembly.Load("Flit.Infrastructure").GetTypes()
            .Single(t => t.Name == "ExternalSyncReadScope");

        tipo.IsPublic.Should().BeFalse("otro ensamblado (API, aplicación) no debe poder instanciarlo");
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

            yield return (Path.GetRelativePath(src, file).Replace('\\', '/'), SinComentarios(File.ReadAllText(file)));
        }
    }

    /// <summary>La documentación de otros tipos puede nombrar el ámbito: se vigila el código, no los comentarios.</summary>
    private static string SinComentarios(string fuente) =>
        string.Join('\n', BloqueDeComentario.Replace(fuente, string.Empty)
            .Split('\n')
            .Where(l => !l.TrimStart().StartsWith("//", StringComparison.Ordinal)));

    private static string DirectorioDeFuentes([CallerFilePath] string esteArchivo = "")
    {
        var dir = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(esteArchivo)!, "..", "..", "..", "src"));
        Directory.Exists(dir).Should().BeTrue($"no se encontró el directorio de fuentes en {dir}");
        return dir;
    }
}
