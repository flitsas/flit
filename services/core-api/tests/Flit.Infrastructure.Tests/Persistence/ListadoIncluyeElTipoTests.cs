using System.Reflection;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace Flit.Infrastructure.Tests.Persistence;

/// <summary>
/// ADR-0050 — todo método del repositorio que devuelva expedientes debe cargar su TIPO.
///
/// <para>La clasificación (familia, código y nombre del trámite) dejó de ser columnas propias y se
/// deriva de <c>ProcedureType</c>, con un fallo ruidoso si la navegación no viene cargada. Eso
/// convierte un <c>Include</c> olvidado en un error de ejecución, no de compilación: el listado de
/// trámites de la consola murió con «navegación ProcedureType no cargada» y en pantalla se vio
/// «Error al cargar trámites», sin pista de la causa.</para>
///
/// <para>Verificación ESTÁTICA sobre el texto del repositorio: la suite no levanta Postgres, y los
/// tests de handler usan fixtures donde el tipo siempre está asignado — por eso ninguno lo detectó.
/// </para>
/// </summary>
public sealed class ListadoIncluyeElTipoTests
{
    /// <summary>
    /// Métodos que devuelven expedientes y cuyos consumidores leen la clasificación. No es toda la
    /// superficie del repositorio a propósito: enumerarlos obliga a decidir explícitamente cuando se
    /// añade uno nuevo, en vez de confiar en una heurística sobre el nombre.
    /// </summary>
    public static TheoryData<string> MetodosQueProyectanExpedientes() =>
    [
        "ListWithSummaryGraphAsync",
        "ListWithSummaryGraphFilteredAsync",
        "ListDraftFinalizedByActorAsync",
    ];

    private static string Fuente()
    {
        // El archivo se localiza desde el ensamblado de tests para no depender del directorio actual.
        var dir = new DirectoryInfo(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src")))
            dir = dir.Parent;

        dir.Should().NotBeNull("la raíz de core-api debe encontrarse desde el ensamblado de tests");

        var ruta = Path.Combine(
            dir!.FullName, "src", "Flit.Infrastructure", "Persistence", "Repositories",
            "ProcedureInstanceRepository.cs");
        File.Exists(ruta).Should().BeTrue($"debe existir {ruta}");
        return File.ReadAllText(ruta);
    }

    [Theory]
    [MemberData(nameof(MetodosQueProyectanExpedientes))]
    public void CargaLaNavegacionDelTipo(string metodo)
    {
        var fuente = Fuente();

        var inicio = fuente.IndexOf(metodo + "(", StringComparison.Ordinal);
        inicio.Should().BeGreaterThan(0, $"el método {metodo} debe existir en el repositorio");

        // El cuerpo llega hasta el siguiente miembro público.
        var fin = fuente.IndexOf("\n    public ", inicio, StringComparison.Ordinal);
        var cuerpo = fin > 0 ? fuente[inicio..fin] : fuente[inicio..];

        Regex.IsMatch(cuerpo, @"\.Include\(\w+ => \w+\.ProcedureType\)")
            .Should().BeTrue(
                $"{metodo} proyecta expedientes cuya clasificación se deriva del tipo; sin "
                + "Include(x => x.ProcedureType) revienta en ejecución al leer Family/TypeCode/TypeName");
    }
}
