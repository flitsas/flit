using System.Runtime.CompilerServices;
using System.Xml.Linq;
using FluentAssertions;
using Xunit;

namespace Flit.Admin.Tests.Architecture;

/// <summary>
/// HU #13342 (Epic #13316, ADR-0065) — el módulo de consultas no depende de Trámites: su árbol de referencias de
/// proyecto no alcanza ningún Flit.Tramites.* ni la infraestructura de core-api. Así core-consultas lo puede alojar
/// sin arrastrar Trámites.
/// </summary>
public sealed class ConsultasModuleArchitectureTests
{
    [Fact]
    public void ElModuloDeConsultas_NoReferenciaTramitesNiInfraestructura()
    {
        var closure = Closure(ModuleProject()).Select(Path.GetFileNameWithoutExtension).ToList();

        closure.Should().NotContain(name => name!.StartsWith("Flit.Tramites.", StringComparison.Ordinal));
        closure.Should().NotContain(["Flit.Infrastructure", "Flit.Api", "Flit.Identity.Infrastructure"]);
        closure.Should().BeEquivalentTo(["Flit.Queries.Domain"], "solo los tipos de tiempo compartidos");
    }

    private static IReadOnlyList<string> Closure(string project)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Stack<string>([project]);
        while (pending.TryPop(out var current))
        {
            var dir = Path.GetDirectoryName(current)!;
            foreach (var reference in XDocument.Load(current).Descendants("ProjectReference"))
            {
                var path = Path.GetFullPath(Path.Combine(dir, reference.Attribute("Include")!.Value.Replace('\\', Path.DirectorySeparatorChar)));
                if (seen.Add(path))
                    pending.Push(path);
            }
        }

        return [.. seen];
    }

    private static string ModuleProject([CallerFilePath] string here = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", "..", "..", "src", "Flit.Modules.Consultas", "Flit.Modules.Consultas.csproj"));
}
