using Flit.Infrastructure.Documents.Fur;
using FluentAssertions;
using Xunit;

namespace Flit.Infrastructure.Tests.Documents.Fur;

public sealed class FurCheckboxLayoutTests
{
    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void Stack_Copropietarios_CabeEnElRecuadroDeOchoPuntos(int n)
    {
        const double y = 338.2;
        const double box = 8;
        var (font, first, step) = FurCheckboxLayout.Stack(y, box, n, singleFontSize: 7);
        var fittedFont = font - FurCheckboxLayout.FontBump;
        var lastBottomFitted = first + FurCheckboxLayout.VisualLift + step * (n - 1)
            + fittedFont * FurCheckboxLayout.DescentRatio;

        lastBottomFitted.Should().BeLessThanOrEqualTo(y + box + 0.05);
        first.Should().BeGreaterThanOrEqualTo(y - FurCheckboxLayout.VisualLift);
        font.Should().BeApproximately(fittedFont + FurCheckboxLayout.FontBump, 0.01);
        font.Should().BeGreaterThan(fittedFont);
    }

    [Fact]
    public void Stack_UnSoloMarca_SubeYAumentaCuerpo()
    {
        var (font, first, step) = FurCheckboxLayout.Stack(338.2, 8, 1, 7);
        font.Should().Be(7 + FurCheckboxLayout.FontBump);
        step.Should().Be(0);
        first.Should().BeApproximately(338.2 + 8 * 0.85 - FurCheckboxLayout.VisualLift, 0.01);
    }
}
