using Flit.Admin.Application.Companies.TransitOffices;
using Flit.Admin.Domain.Companies.TransitOffices;
using FluentAssertions;
using Xunit;

namespace Flit.Admin.Tests.Companies.TransitGrants;

/// <summary>
/// Catálogo estático de organismos de tránsito (HU #10192, AC1) — búsqueda
/// case-insensitive e insensible a tildes sobre nombre y código, término vacío
/// devuelve todo, y término sin coincidencias devuelve vacío.
///
/// Uso de ejemplo:
/// <code>
/// var catalog = new StaticTransitOfficeCatalog();
/// var hits = catalog.Search("BOGOTA"); // coincide con "…Bogotá"
/// </code>
/// </summary>
public sealed class StaticTransitOfficeCatalogTests
{
    private readonly StaticTransitOfficeCatalog _catalog = new StaticTransitOfficeCatalog();

    [Fact]
    public void All_HasAtLeastFiveEntries()
    {
        _catalog.All.Should().HaveCountGreaterThanOrEqualTo(5);
    }

    [Theory]
    [InlineData("bogota")]   // sin tilde y minúsculas
    [InlineData("BOGOTÁ")]   // con tilde y mayúsculas
    [InlineData("Bogotá")]
    public void Search_IsCaseAndDiacriticInsensitive_OnName(string term)
    {
        var result = _catalog.Search(term);

        result.Should().ContainSingle()
            .Which.Name.Should().Be("Secretaría de Movilidad Bogotá");
    }

    [Fact]
    public void Search_MatchesByCode()
    {
        var result = _catalog.Search("11001");

        result.Should().ContainSingle().Which.Code.Should().Be("11001");
    }

    [Fact]
    public void Search_ReturnsEmpty_WhenNoMatch()
    {
        _catalog.Search("ciudad-inexistente").Should().BeEmpty();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Search_ReturnsAll_WhenTermIsBlank(string? term)
    {
        _catalog.Search(term).Should().HaveCount(_catalog.All.Count);
    }

    [Fact]
    public void ExistsAndGetById_ResolveKnownOffice()
    {
        var known = _catalog.All[0];

        _catalog.Exists(known.Id).Should().BeTrue();
        _catalog.GetById(known.Id).Should().Be(known);
    }

    [Fact]
    public void ExistsAndGetById_RejectUnknownOffice()
    {
        var unknown = Guid.NewGuid();

        _catalog.Exists(unknown).Should().BeFalse();
        _catalog.GetById(unknown).Should().BeNull();
    }
}
