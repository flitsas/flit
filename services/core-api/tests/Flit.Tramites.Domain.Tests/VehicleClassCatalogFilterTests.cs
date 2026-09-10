using Flit.Tramites.Domain.Tramites.Catalog;
using FluentAssertions;
using Xunit;

namespace Flit.Tramites.Domain.Tests;

public sealed class VehicleClassCatalogFilterTests
{
    private static readonly string[] Known =
    [
        "AUTOMOVIL", "CAMION", "CAMIONETA", "SEMIREMOLQUE",
    ];

    [Fact]
    public void Normalize_QuitaTildesYColapsaEspacios()
    {
        VehicleClassCatalogFilter.Normalize("  Cami\u00F3n  ").Should().Be("CAMION");
    }

    [Fact]
    public void MatchKnownClass_EmpataClaseExacta()
    {
        VehicleClassCatalogFilter.MatchKnownClass("AUTOMOVIL", Known).Should().Be("AUTOMOVIL");
    }

    [Fact]
    public void MatchKnownClass_NoConfundeCamionConCamioneta()
    {
        VehicleClassCatalogFilter.MatchKnownClass("CAMIONETA", Known).Should().Be("CAMIONETA");
        VehicleClassCatalogFilter.MatchKnownClass("CAMION", Known).Should().Be("CAMION");
    }

    [Fact]
    public void MatchKnownClass_PrefijoDePalabra()
    {
        VehicleClassCatalogFilter.MatchKnownClass("CAMION CISTERNA", Known).Should().Be("CAMION");
    }

    [Fact]
    public void MatchKnownClass_AliasSemirremolque()
    {
        VehicleClassCatalogFilter.MatchKnownClass("SEMIRREMOLQUE", Known).Should().Be("SEMIREMOLQUE");
    }

    [Fact]
    public void MatchKnownClass_Desconocida_EsNull()
    {
        VehicleClassCatalogFilter.MatchKnownClass("ANFIBIO", Known).Should().BeNull();
    }
}
