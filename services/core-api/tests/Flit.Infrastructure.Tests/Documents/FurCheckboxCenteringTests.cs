using Flit.Infrastructure.Documents.Fur;
using Flit.Tramites.Application.Documents;
using FluentAssertions;
using Xunit;

namespace Flit.Infrastructure.Tests.Documents;

/// <summary>
/// HU #14731 (Épica #14267) — las X del FUR deben quedar centradas en su casilla en los tres
/// formatos (AUTOMOTOR/MAQUINARIA/REMOLQUES), con 1 a 4 X apiladas dentro del recuadro sin solaparse.
/// </summary>
public sealed class FurCheckboxCenteringTests
{
    private const double Tol = 0.01;

    public static IEnumerable<object[]> CasillasPorFormato()
    {
        foreach (var format in new[] { FurTemplateFormat.Automotor, FurTemplateFormat.Maquinaria, FurTemplateFormat.Remolques })
        {
            var m = FurFieldManifestLoader.LoadEmbedded(format);
            foreach (var f in m.Fields.Where(f => f.Type == FurFieldType.Checkbox))
                for (var count = 1; count <= 4; count++)
                    yield return new object[] { format, f.Id, count };
        }
    }

    [Theory]
    [MemberData(nameof(CasillasPorFormato))]
    public void Place_CentersWithinBoxWithoutOverlap(FurTemplateFormat format, string id, int count)
    {
        var f = FurFieldManifestLoader.LoadEmbedded(format).Fields.Single(x => x.Id == id);
        var size = f.Size > 0 ? f.Size : 9;

        var items = FurCheckboxLayout.Place(f.X, f.Y, size, count, f.FontSize);

        items.Should().HaveCount(count);
        items.Select(i => i.FontSize).Distinct().Should().HaveCount(1, "todas las X comparten cuerpo");

        var stripH = size / count;
        var glyphs = new List<(double Top, double Bottom)>();
        for (var i = 0; i < count; i++)
        {
            var p = items[i];
            var w = FurCheckboxLayout.GlyphWidthRatio * p.FontSize;
            var h = FurCheckboxLayout.CapHeightRatio * p.FontSize;
            var cx = p.LeftX + w / 2;
            var cy = p.BaselineY - h / 2;

            cx.Should().BeApproximately(f.X + size / 2, Tol, $"{format}/{id} X#{i} centrada en horizontal");
            cy.Should().BeApproximately(f.Y + stripH * (i + 0.5), Tol, $"{format}/{id} X#{i} centrada en su franja");

            p.LeftX.Should().BeGreaterThanOrEqualTo(f.X - Tol);
            (p.LeftX + w).Should().BeLessThanOrEqualTo(f.X + size + Tol);
            (p.BaselineY - h).Should().BeGreaterThanOrEqualTo(f.Y - Tol);
            p.BaselineY.Should().BeLessThanOrEqualTo(f.Y + size + Tol);

            glyphs.Add((p.BaselineY - h, p.BaselineY));
        }

        for (var i = 1; i < glyphs.Count; i++)
            glyphs[i].Top.Should().BeGreaterThanOrEqualTo(glyphs[i - 1].Bottom - Tol, $"{format}/{id} sin solape entre X");
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-3, 1)]
    [InlineData(1, 1)]
    [InlineData(4, 4)]
    [InlineData(7, 4)]
    public void Place_ClampsCountToOneToFour(int count, int expected)
    {
        FurCheckboxLayout.Place(100, 200, 9, count, 7).Should().HaveCount(expected);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-5, 3)]
    public void Place_NonPositiveSize_DoesNotThrowNorProduceNaN(double size, int count)
    {
        var items = FurCheckboxLayout.Place(50, 60, size, count, 7);

        items.Should().NotBeEmpty();
        foreach (var p in items)
        {
            double.IsFinite(p.FontSize).Should().BeTrue();
            double.IsFinite(p.LeftX).Should().BeTrue();
            double.IsFinite(p.BaselineY).Should().BeTrue();
        }
    }
}
