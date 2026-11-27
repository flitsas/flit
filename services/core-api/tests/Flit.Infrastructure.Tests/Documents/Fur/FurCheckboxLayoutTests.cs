using Flit.Infrastructure.Documents.Fur;
using FluentAssertions;
using Xunit;

namespace Flit.Infrastructure.Tests.Documents.Fur;

public sealed class FurCheckboxLayoutTests
{
    [Fact]
    public void Place_UnaX_QuedaCentradaEnLaCasilla()
    {
        var p = FurCheckboxLayout.Place(100, 338.2, 8, 1, 9).Single();
        (p.LeftX + FurCheckboxLayout.GlyphWidthRatio * p.FontSize / 2).Should().BeApproximately(104, 0.001);
        (p.BaselineY - FurCheckboxLayout.CapHeightRatio * p.FontSize / 2).Should().BeApproximately(342.2, 0.001);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void Place_Copropietarios_CadaXCentradaEnSuFranjaSinSalirse(int n)
    {
        const double x = 100, y = 338.2, size = 8;
        var list = FurCheckboxLayout.Place(x, y, size, n, 9);
        list.Should().HaveCount(n);
        var strip = size / n;
        for (var i = 0; i < n; i++)
        {
            var p = list[i];
            var cy = p.BaselineY - FurCheckboxLayout.CapHeightRatio * p.FontSize / 2;
            cy.Should().BeApproximately(y + strip * (i + 0.5), 0.001);
            (p.BaselineY - FurCheckboxLayout.CapHeightRatio * p.FontSize).Should().BeGreaterThanOrEqualTo(y + strip * i);
            p.BaselineY.Should().BeLessThanOrEqualTo(y + strip * (i + 1));
            p.LeftX.Should().BeGreaterThanOrEqualTo(x);
            (p.LeftX + FurCheckboxLayout.GlyphWidthRatio * p.FontSize).Should().BeLessThanOrEqualTo(x + size);
            p.FontSize.Should().Be(list[0].FontSize);
        }
    }

    [Fact]
    public void Place_TamanoInvalidoYCantidadFueraDeRango_SeAcotan()
    {
        FurCheckboxLayout.Place(0, 0, 0, 9, 9).Should().HaveCount(4);
        FurCheckboxLayout.Place(0, 0, -1, 0, 9).Should().HaveCount(1);
    }
}
