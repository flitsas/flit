using Flit.Analytics.Application.IctQueries;
using FluentAssertions;
using Xunit;

namespace Flit.Analytics.Application.Tests.IctQueries;

/// <summary>
/// Las 3 consultas de fábrica de ICT: existen para que el constructor de consultas nunca se abra en
/// blanco. Lo que importa probar es que sus ids son estables (el enlace compartible depende de eso)
/// y que no chocan con los prefijos ya usados por empresa (<c>e0000000...</c>) ni por el organismo
/// (<c>f0000000...</c>).
/// </summary>
public sealed class IctFactoryQueriesTests
{
    [Fact]
    public void Queries_TieneExactamenteTres()
    {
        IctFactoryQueries.Queries.Should().HaveCount(3);
    }

    [Fact]
    public void Queries_TodasMarcadasDeFabrica()
    {
        IctFactoryQueries.Queries.Should().OnlyContain(q => q.DeFabrica);
    }

    [Fact]
    public void Queries_IdsUnicosYConPrefijoPropio()
    {
        var ids = IctFactoryQueries.Queries.Select(q => q.Id).ToList();

        ids.Should().OnlyHaveUniqueItems();
        ids.Should().OnlyContain(id => id.ToString().StartsWith("1c700000-", StringComparison.Ordinal));
    }

    [Fact]
    public void IsFactory_ConUnIdDeFabrica_DevuelveTrue()
    {
        var id = IctFactoryQueries.Queries[0].Id;

        IctFactoryQueries.IsFactory(id).Should().BeTrue();
    }

    [Fact]
    public void IsFactory_ConUnIdCualquiera_DevuelveFalse()
    {
        IctFactoryQueries.IsFactory(Guid.NewGuid()).Should().BeFalse();
    }

    [Fact]
    public void ConNovedadesEstaSemana_FiltraPorTieneNovedades()
    {
        var query = IctFactoryQueries.Queries.Single(q => q.Nombre == "Con novedades esta semana");

        query.Definition.Condiciones.Should().ContainSingle(c =>
            c.FieldId == IctQueryFieldCatalog.TieneNovedades && c.Values.Contains("true"));
    }

    [Fact]
    public void AunSinBorrador_FiltraPorTieneBorradorFalse()
    {
        var query = IctFactoryQueries.Queries.Single(q => q.Nombre == "Aún sin borrador");

        query.Definition.Condiciones.Should().ContainSingle(c =>
            c.FieldId == IctQueryFieldCatalog.TieneBorrador && c.Values.Contains("false"));
    }
}
