using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace Flit.Impuestos.Tests.Aislamiento;

/// <summary>
/// HU #14694 (M05, plan §5.1) — prueba de arquitectura: en el código fuente del servicio (<c>services/core-impuestos/src</c>)
/// una llamada a <c>IgnoreQueryFilters()</c> solo puede estar en <c>Modulos/Backoffice/</c>, que lee entre compañías con la
/// empresa explícita y auditoría. Los comentarios no cuentan; <c>bin/</c> y <c>obj/</c> quedan fuera. En M05 todavía no hay
/// backoffice: no debe haber ningún uso.
/// </summary>
/// <example>
/// <code>
/// dotnet test services/core-impuestos/tests/Flit.Impuestos.Tests --no-build --filter FullyQualifiedName~IgnoreQueryFiltersSoloEnBackofficeTests
/// </code>
/// </example>
public sealed partial class IgnoreQueryFiltersSoloEnBackofficeTests
{
    private const string ModuloPermitido = "Modulos/Backoffice/";

    [Fact]
    public void Ningun_otro_modulo_usa_IgnoreQueryFilters()
    {
        var src = Path.Combine(RaizDelServicio(), "src");
        Directory.Exists(Path.Combine(src, "Flit.Impuestos.Api")).Should().BeTrue("el escáner mira el código del servicio");

        Usos(src).Where(u => !u.Archivo.Contains(ModuloPermitido, StringComparison.Ordinal)).Select(u => $"{u.Archivo}:{u.Linea}")
            .Should().BeEmpty("IgnoreQueryFilters() solo se permite en el módulo Backoffice (con empresa explícita y auditoría)");
    }

    [Fact]
    public void El_escaner_detecta_un_uso_fuera_del_modulo_Backoffice()
    {
        var raiz = Directory.CreateTempSubdirectory("imp-arq-").FullName;
        try
        {
            Escribir(raiz, "Modulos/Liquidaciones/Lista.cs", "var x = db.Set<T>()\n    .IgnoreQueryFilters ()\n    .ToList();");
            Escribir(raiz, "Modulos/Backoffice/Lectura.cs", "db.Set<T>().IgnoreQueryFilters().Where(e => e.TenantId == empresa);");
            Escribir(raiz, "Modulos/Comun/Doc.cs", "/// <summary>Solo el backoffice usa <c>.IgnoreQueryFilters()</c>.</summary>\n// db.IgnoreQueryFilters();");
            Escribir(raiz, "obj/Debug/Generado.cs", "db.Set<T>().IgnoreQueryFilters();");

            Usos(raiz).Select(u => $"{u.Archivo}:{u.Linea}").Should().BeEquivalentTo(["Modulos/Liquidaciones/Lista.cs:2", "Modulos/Backoffice/Lectura.cs:1"]);
        }
        finally
        {
            Directory.Delete(raiz, recursive: true);
        }
    }

    /// <summary>Líneas de los <c>.cs</c> bajo <paramref name="raiz"/> que llaman a <c>IgnoreQueryFilters(</c>, sin comentarios de línea.</summary>
    internal static IReadOnlyList<(string Archivo, int Linea)> Usos(string raiz)
    {
        var usos = new List<(string, int)>();
        foreach (var archivo in Directory.EnumerateFiles(raiz, "*.cs", SearchOption.AllDirectories))
        {
            var relativo = Path.GetRelativePath(raiz, archivo).Replace('\\', '/');
            if (EsGenerado(relativo))
                continue;
            var lineas = File.ReadAllLines(archivo);
            for (var i = 0; i < lineas.Length; i++)
            {
                var codigo = lineas[i];
                var comentario = codigo.IndexOf("//", StringComparison.Ordinal);
                if (comentario >= 0)
                    codigo = codigo[..comentario];
                if (Llamada().IsMatch(codigo))
                    usos.Add((relativo, i + 1));
            }
        }

        return usos;
    }

    internal static bool EsGenerado(string relativo) =>
        relativo.StartsWith("bin/", StringComparison.Ordinal) || relativo.StartsWith("obj/", StringComparison.Ordinal)
        || relativo.Contains("/bin/", StringComparison.Ordinal) || relativo.Contains("/obj/", StringComparison.Ordinal);

    internal static string RaizDelServicio()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Flit.Impuestos.slnx")))
                return dir.FullName;
        }

        throw new InvalidOperationException("No se encontró Flit.Impuestos.slnx subiendo desde " + AppContext.BaseDirectory);
    }

    internal static void Escribir(string raiz, string relativo, string contenido)
    {
        var ruta = Path.Combine(raiz, relativo);
        Directory.CreateDirectory(Path.GetDirectoryName(ruta)!);
        File.WriteAllText(ruta, contenido);
    }

    [GeneratedRegex(@"\bIgnoreQueryFilters\s*\(")]
    private static partial Regex Llamada();
}
