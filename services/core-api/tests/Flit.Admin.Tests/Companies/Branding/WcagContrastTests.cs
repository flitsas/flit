using System.Text.Json;
using System.Text.Json.Serialization;
using Flit.Admin.Domain.Companies.Branding;
using FluentAssertions;
using Xunit;

namespace Flit.Admin.Tests.Companies.Branding;

/// <summary>
/// Uso de ejemplo:
/// var ratio = WcagContrast.RoundedContrastRatio("#FFFFFF", "#162744"); // 14.92
/// WcagContrast.MeetsMinimum(ratio); // true
/// HU #12413 AC4 — mismo fixture que <c>frontend/lib/brand/__tests__/contrast.test.ts</c> (#12414),
/// vía <c>tests/Shared/Fixtures/Branding/contrast-cases.json</c>, para paridad backend/frontend.
/// </summary>
public sealed class WcagContrastTests
{
    private sealed record ContrastCase(
        [property: JsonPropertyName("fg")] string Fg,
        [property: JsonPropertyName("bg")] string Bg,
        [property: JsonPropertyName("expectedRatio")] double ExpectedRatio);

    public static TheoryData<string, string, double> FixtureCases()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Branding", "contrast-cases.json");
        var json = File.ReadAllText(path);
        var cases = JsonSerializer.Deserialize<List<ContrastCase>>(json)
            ?? throw new InvalidOperationException("Fixture contrast-cases.json vacío o inválido.");

        var data = new TheoryData<string, string, double>();
        foreach (var c in cases)
        {
            data.Add(c.Fg, c.Bg, c.ExpectedRatio);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(FixtureCases))]
    public void RoundedContrastRatio_CoincideConElFixtureCompartido(string fg, string bg, double expectedRatio)
    {
        WcagContrast.RoundedContrastRatio(fg, bg).Should().BeApproximately(expectedRatio, 0.01);
    }

    [Fact]
    public void ContrastRatio_EsSimetrico()
    {
        WcagContrast.ContrastRatio("#0B3D91", "#FFFFFF")
            .Should().BeApproximately(WcagContrast.ContrastRatio("#FFFFFF", "#0B3D91"), 0.0001);
    }

    [Fact]
    public void ContrastRatio_MismoColor_Da1a1()
    {
        WcagContrast.ContrastRatio("#557EFF", "#557EFF").Should().BeApproximately(1.0, 0.0001);
    }

    [Fact]
    public void ContrastRatio_BlancoSobreNegro_DaElMaximo()
    {
        WcagContrast.ContrastRatio("#FFFFFF", "#000000").Should().BeApproximately(21.0, 0.01);
    }

    [Theory]
    [InlineData(14.92, 4.5, true)]
    [InlineData(3.61, 4.5, false)]
    [InlineData(4.5, 4.5, true)] // límite exacto — cumple (AC4: "menor que 4,5 a 1" rechaza, 4.5 exacto no)
    public void MeetsMinimum_RespetaElUmbralParametrizable(double ratio, double min, bool expected)
    {
        WcagContrast.MeetsMinimum(ratio, min).Should().Be(expected);
    }

    [Fact]
    public void MeetsMinimum_UsaElUmbralPorDefectoSiNoSeIndica()
    {
        WcagContrast.MeetsMinimum(4.5).Should().BeTrue();
        WcagContrast.MeetsMinimum(4.49).Should().BeFalse();
    }
}
