using System.Text.RegularExpressions;
using System.Xml.Linq;
using Flit.Impuestos.Tests.Aislamiento;
using FluentAssertions;
using Xunit;

namespace Flit.Impuestos.Tests.Arquitectura;

/// <summary>
/// HU #14694 (M05, EP-CF1) — producto independiente: ningún proyecto de <c>services/core-impuestos</c> referencia
/// <c>core-diagnostico</c>, un proyecto <c>Flit.Diagnostico.*</c> ni el de <c>core-consultas</c> que contiene <c>Impuesto/</c>
/// (<c>Flit.Consultas.Api</c>), y ningún <c>.cs</c> de <c>src/</c> importa esos espacios de nombres. Los contratos gRPC públicos
/// (<c>*.Grpc.Contracts</c>) sí se pueden usar. Los datos de referencia públicos no son código reutilizado.
/// </summary>
/// <example>
/// <code>
/// dotnet test services/core-impuestos/tests/Flit.Impuestos.Tests --no-build --filter FullyQualifiedName~IndependenciaTests
/// </code>
/// </example>
public sealed partial class IndependenciaTests
{
    [Fact]
    public void Ningun_proyecto_referencia_diagnostico_ni_el_api_de_consultas()
    {
        var raiz = IgnoreQueryFiltersSoloEnBackofficeTests.RaizDelServicio();
        var proyectos = Directory.EnumerateFiles(raiz, "*.csproj", SearchOption.AllDirectories)
            .Where(p => !IgnoreQueryFiltersSoloEnBackofficeTests.EsGenerado(Path.GetRelativePath(raiz, p).Replace('\\', '/'))).ToList();

        proyectos.Should().HaveCountGreaterThanOrEqualTo(4, "Api, Domain, Grpc.Contracts y Tests");
        ReferenciasProhibidas(raiz).Should().BeEmpty("EP-CF1: Impuestos no reutiliza código de Diagnóstico ni de core-consultas/Impuesto");
    }

    [Fact]
    public void Ningun_archivo_de_src_importa_diagnostico_ni_consultas_impuesto()
    {
        var src = Path.Combine(IgnoreQueryFiltersSoloEnBackofficeTests.RaizDelServicio(), "src");

        UsosProhibidos(src).Should().BeEmpty("EP-CF1: sin using ni nombres calificados hacia Flit.Diagnostico.* o Flit.Consultas.Api.Impuesto");
    }

    [Fact]
    public void El_escaner_detecta_referencias_y_usos_prohibidos()
    {
        var raiz = Directory.CreateTempSubdirectory("imp-ind-").FullName;
        try
        {
            IgnoreQueryFiltersSoloEnBackofficeTests.Escribir(raiz, "src/A/A.csproj",
                "<Project><ItemGroup><ProjectReference Include=\"..\\..\\..\\core-diagnostico\\src\\Flit.Diagnostico.Domain\\Flit.Diagnostico.Domain.csproj\" />" +
                "<ProjectReference Include=\"..\\..\\..\\core-consultas\\src\\Flit.Consultas.Grpc.Contracts\\Flit.Consultas.Grpc.Contracts.csproj\" /></ItemGroup></Project>");
            IgnoreQueryFiltersSoloEnBackofficeTests.Escribir(raiz, "src/B/B.csproj",
                "<Project><ItemGroup><ProjectReference Include=\"../../../core-consultas/src/Flit.Consultas.Api/Flit.Consultas.Api.csproj\" /></ItemGroup></Project>");
            IgnoreQueryFiltersSoloEnBackofficeTests.Escribir(raiz, "src/A/Uno.cs", "using Flit.Diagnostico.Domain.Consultas;\nnamespace X;");
            IgnoreQueryFiltersSoloEnBackofficeTests.Escribir(raiz, "src/A/Dos.cs", "var x = new Flit.Consultas.Api.Impuesto.CatalogoDepartamentosImpuesto();");
            IgnoreQueryFiltersSoloEnBackofficeTests.Escribir(raiz, "src/A/Tres.cs", "// using Flit.Diagnostico.Api;\nusing Flit.Consultas.Grpc.V1;");

            ReferenciasProhibidas(raiz).Should().HaveCount(2);
            UsosProhibidos(Path.Combine(raiz, "src")).Select(u => $"{u.Archivo}:{u.Linea}").Should().BeEquivalentTo(["A/Uno.cs:1", "A/Dos.cs:1"]);
        }
        finally
        {
            Directory.Delete(raiz, recursive: true);
        }
    }

    /// <summary><c>ProjectReference</c> prohibidas en los <c>.csproj</c> bajo <paramref name="raiz"/>.</summary>
    internal static IReadOnlyList<string> ReferenciasProhibidas(string raiz)
    {
        var prohibidas = new List<string>();
        foreach (var proyecto in Directory.EnumerateFiles(raiz, "*.csproj", SearchOption.AllDirectories))
        {
            if (IgnoreQueryFiltersSoloEnBackofficeTests.EsGenerado(Path.GetRelativePath(raiz, proyecto).Replace('\\', '/')))
                continue;
            foreach (var referencia in XDocument.Load(proyecto).Descendants("ProjectReference").Select(r => (string?)r.Attribute("Include") ?? string.Empty))
            {
                var normal = referencia.Replace('\\', '/');
                if (normal.Contains("core-diagnostico/", StringComparison.OrdinalIgnoreCase)
                    || normal.Contains("Flit.Diagnostico.", StringComparison.OrdinalIgnoreCase)
                    || normal.Contains("/Flit.Consultas.Api/", StringComparison.OrdinalIgnoreCase))
                    prohibidas.Add($"{Path.GetFileName(proyecto)} → {referencia}");
            }
        }

        return prohibidas;
    }

    /// <summary>Líneas de los <c>.cs</c> bajo <paramref name="src"/> que nombran un espacio prohibido, sin comentarios de línea.</summary>
    internal static IReadOnlyList<(string Archivo, int Linea)> UsosProhibidos(string src)
    {
        var usos = new List<(string, int)>();
        foreach (var archivo in Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories))
        {
            var relativo = Path.GetRelativePath(src, archivo).Replace('\\', '/');
            if (IgnoreQueryFiltersSoloEnBackofficeTests.EsGenerado(relativo))
                continue;
            var lineas = File.ReadAllLines(archivo);
            for (var i = 0; i < lineas.Length; i++)
            {
                var codigo = lineas[i];
                var comentario = codigo.IndexOf("//", StringComparison.Ordinal);
                if (comentario >= 0)
                    codigo = codigo[..comentario];
                if (Prohibido().IsMatch(codigo))
                    usos.Add((relativo, i + 1));
            }
        }

        return usos;
    }

    [GeneratedRegex(@"\bFlit\.Diagnostico\b|\bFlit\.Consultas\.Api\.Impuesto\b")]
    private static partial Regex Prohibido();
}
