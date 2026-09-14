using FluentAssertions;
using Xunit;

namespace Flit.Integration.Tests.Tenancy;

/// <summary>
/// HU #12322 (AC6) — el inventario de consultas cubiertas es un artefacto revisable: cada entrada de
/// <see cref="CoveredQueries.All"/> y <see cref="CoveredQueries.Dedicated"/> aparece en la tabla
/// «Cobertura #12322» del README del proyecto, y los códigos son únicos. No necesita PostgreSQL.
/// <para>Uso de ejemplo: al añadir <c>Q28</c> al inventario, esta prueba exige documentarlo en el README.</para>
/// </summary>
public sealed class CoveredQueriesInventoryTests
{
    [Fact]
    public void Los_codigos_del_inventario_son_unicos_y_consecutivos()
    {
        var ids = CoveredQueries.All.Concat(CoveredQueries.Dedicated).Select(q => q.Id).ToList();

        ids.Should().OnlyHaveUniqueItems();
        ids.Should().Equal(Enumerable.Range(1, ids.Count).Select(n => $"Q{n:D2}"));
    }

    [Fact]
    public void Cada_consulta_cubierta_esta_documentada_en_el_README()
    {
        var readme = File.ReadAllText(LocateReadme());

        foreach (var query in CoveredQueries.All.Concat(CoveredQueries.Dedicated))
        {
            readme.Should().Contain($"| {query.Id} |", $"{query} debe estar en la tabla «Cobertura #12322»");
        }
    }

    private static string LocateReadme()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "README.md");
            if (File.Exists(candidate) && File.ReadAllText(candidate).Contains("Cobertura #12322", StringComparison.Ordinal))
                return candidate;
            dir = dir.Parent;
        }

        throw new FileNotFoundException("No se encontró el README del proyecto con la sección «Cobertura #12322».");
    }
}
