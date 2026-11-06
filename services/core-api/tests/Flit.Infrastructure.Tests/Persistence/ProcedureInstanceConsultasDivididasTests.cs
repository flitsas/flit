using System.Reflection;
using FluentAssertions;
using Xunit;

namespace Flit.Infrastructure.Tests.Persistence;

/// <summary>
/// Verificación ESTÁTICA (la suite no levanta Postgres): los grafos del trámite que incluyen varias
/// colecciones usan consulta dividida. Sin ella, las colecciones se multiplican entre sí en un solo JOIN:
/// en PDN el detalle devolvía ~2.800 filas por trámite y llegó a tardar 46 s.
/// </summary>
public sealed class ProcedureInstanceConsultasDivididasTests
{
    [Theory]
    [InlineData("GetByIdWithDetailsAsync", 2)]
    [InlineData("GetByIdWithFurGraphAsync", 1)]
    public void ElGrafoConVariasColeccionesUsaConsultaDividida(string metodo, int sobrecargas)
    {
        var fuente = Fuente();
        var cuerpos = new List<string>();
        var inicio = 0;
        while ((inicio = fuente.IndexOf(" " + metodo + "(", inicio, StringComparison.Ordinal)) > 0)
        {
            var fin = fuente.IndexOf("\n    public ", inicio + 1, StringComparison.Ordinal);
            cuerpos.Add(fin > 0 ? fuente[inicio..fin] : fuente[inicio..]);
            inicio++;
        }

        cuerpos.Should().HaveCount(sobrecargas);
        cuerpos.Should().AllSatisfy(c => c.Should().Contain(".AsSplitQuery()"));
    }

    private static string Fuente()
    {
        var dir = new DirectoryInfo(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src")))
            dir = dir.Parent;
        dir.Should().NotBeNull();
        return File.ReadAllText(Path.Combine(
            dir!.FullName, "src", "Flit.Infrastructure", "Persistence", "Repositories", "ProcedureInstanceRepository.cs"))
            .Replace("\r\n", "\n", StringComparison.Ordinal);
    }
}
