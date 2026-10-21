using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace Flit.Diagnostico.Tests.Aislamiento;

/// <summary>
/// HU #13728 (F00.1-H5), escenario 5 — prueba de arquitectura (grep auditable): en el código fuente del servicio
/// (<c>services/core-diagnostico/src</c>), una llamada a <c>IgnoreQueryFilters()</c> solo puede estar en el módulo
/// Backoffice (<c>Modulos/Backoffice/</c>), que lee entre compañías con la empresa explícita y auditoría (ADR-0072 punto 3).
/// Los comentarios no cuentan; <c>bin/</c> y <c>obj/</c> quedan fuera.
/// </summary>
/// <example>
/// <code>
/// dotnet test services/core-diagnostico/tests/Flit.Diagnostico.Tests --no-build --filter FullyQualifiedName~IgnoreQueryFiltersSoloEnBackofficeTests
/// </code>
/// </example>
public sealed partial class IgnoreQueryFiltersSoloEnBackofficeTests
{
    private const string ModuloPermitido = "Modulos/Backoffice/";

    // Escenario 5 — IgnoreQueryFilters() solo aparece en el módulo Backoffice.
    [Fact]
    public void Ningun_otro_modulo_usa_IgnoreQueryFilters()
    {
        var src = Path.Combine(RaizDelServicio(), "src");

        var usos = Usos(src);

        usos.Should().NotBeEmpty("el backoffice lee entre compañías con IgnoreQueryFilters(): si no hay usos, el escáner no mira donde debe");
        usos.Where(u => !u.Archivo.Contains(ModuloPermitido, StringComparison.Ordinal)).Select(u => $"{u.Archivo}:{u.Linea}")
            .Should().BeEmpty("IgnoreQueryFilters() solo se permite en el módulo Backoffice (con empresa explícita y auditoría)");
    }

    // Escenario 5 — el escáner detecta un uso fuera del módulo, acepta el del módulo y no cuenta los comentarios.
    [Fact]
    public void El_escaner_detecta_un_uso_fuera_del_modulo_Backoffice()
    {
        var raiz = Directory.CreateTempSubdirectory("dgn-arq-").FullName;
        try
        {
            Escribir(raiz, "Modulos/Diagnosticos/Lista.cs", "var x = db.Set<T>()\n    .IgnoreQueryFilters ()\n    .ToList();");
            Escribir(raiz, "Modulos/Backoffice/Lectura.cs", "db.Set<T>().IgnoreQueryFilters().Where(e => e.TenantId == empresa);");
            Escribir(raiz, "Modulos/Comun/Doc.cs", "/// <summary>Solo el backoffice usa <c>.IgnoreQueryFilters()</c>.</summary>\n// db.IgnoreQueryFilters();");
            Escribir(raiz, "obj/Debug/Generado.cs", "db.Set<T>().IgnoreQueryFilters();");

            var usos = Usos(raiz);

            usos.Select(u => $"{u.Archivo}:{u.Linea}").Should().BeEquivalentTo(["Modulos/Diagnosticos/Lista.cs:2", "Modulos/Backoffice/Lectura.cs:1"]);
        }
        finally
        {
            Directory.Delete(raiz, recursive: true);
        }
    }

    /// <summary>Llamadas a <c>IgnoreQueryFilters(</c> en los <c>.cs</c> bajo <paramref name="raiz"/>, sin comentarios de línea.</summary>
    internal static IReadOnlyList<(string Archivo, int Linea)> Usos(string raiz)
    {
        var usos = new List<(string, int)>();
        foreach (var archivo in Directory.EnumerateFiles(raiz, "*.cs", SearchOption.AllDirectories))
        {
            var relativo = Path.GetRelativePath(raiz, archivo).Replace('\\', '/');
            if (relativo.StartsWith("bin/", StringComparison.Ordinal) || relativo.StartsWith("obj/", StringComparison.Ordinal)
                || relativo.Contains("/bin/", StringComparison.Ordinal) || relativo.Contains("/obj/", StringComparison.Ordinal))
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

    private static string RaizDelServicio()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Flit.Diagnostico.slnx")))
                return dir.FullName;
        }

        throw new InvalidOperationException("No se encontró Flit.Diagnostico.slnx subiendo desde " + AppContext.BaseDirectory);
    }

    private static void Escribir(string raiz, string relativo, string contenido)
    {
        var ruta = Path.Combine(raiz, relativo);
        Directory.CreateDirectory(Path.GetDirectoryName(ruta)!);
        File.WriteAllText(ruta, contenido);
    }

    [GeneratedRegex(@"\bIgnoreQueryFilters\s*\(")]
    private static partial Regex Llamada();
}
