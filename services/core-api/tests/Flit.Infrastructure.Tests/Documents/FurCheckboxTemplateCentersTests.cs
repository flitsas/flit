using System.Text.Json;
using Flit.Infrastructure.Documents.Fur;
using Flit.Tramites.Application.Documents;
using FluentAssertions;
using Xunit;

namespace Flit.Infrastructure.Tests.Documents;

/// <summary>
/// HU #14731 (Épica #14267) — comprueba, contra la casilla IMPRESA en la plantilla, que el manifiesto
/// y el layout de las X quedan centrados. Referencia NO circular: <c>fur-casillas-centros.json</c> se
/// generó midiendo los trazos de la plantilla PDF (no leyendo el manifiesto), por lo que detecta un
/// x/y/size del manifiesto desplazado respecto de la casilla real. Si se agrega una casilla nueva al
/// manifiesto, hay que medirla en la plantilla y añadirla al JSON.
/// </summary>
public sealed class FurCheckboxTemplateCentersTests
{
    private const double Tol = 0.6;
    private const int TotalEsperado = 143;

    private static readonly Lazy<Dictionary<string, Dictionary<string, (double Cx, double Cy)>>> Centros = new(Cargar);

    private static Dictionary<string, Dictionary<string, (double Cx, double Cy)>> Cargar()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Documents", "fur-casillas-centros.json");
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var result = new Dictionary<string, Dictionary<string, (double, double)>>();
        foreach (var fmt in doc.RootElement.EnumerateObject())
        {
            var d = new Dictionary<string, (double, double)>();
            foreach (var c in fmt.Value.EnumerateObject())
                d[c.Name] = (c.Value.GetProperty("cx").GetDouble(), c.Value.GetProperty("cy").GetDouble());
            result[fmt.Name] = d;
        }
        return result;
    }

    private static readonly FurTemplateFormat[] Formatos =
        { FurTemplateFormat.Automotor, FurTemplateFormat.Maquinaria, FurTemplateFormat.Remolques };

    public static IEnumerable<object[]> Casillas()
    {
        foreach (var format in Formatos)
            foreach (var f in FurFieldManifestLoader.LoadEmbedded(format).Fields.Where(f => f.Type == FurFieldType.Checkbox))
                yield return new object[] { format, f.Id };
    }

    private static (double Cx, double Cy) Medido(FurTemplateFormat format, string id)
    {
        Centros.Value.TryGetValue(format.ToString(), out var porFormato).Should().BeTrue($"el JSON debe tener el formato {format}");
        porFormato!.TryGetValue(id, out var c).Should().BeTrue(
            $"{format}/{id}: la casilla no está en fur-casillas-centros.json; hay que medir la casilla nueva en la plantilla y agregarla");
        return c;
    }

    [Theory]
    [MemberData(nameof(Casillas))]
    public void CentroDelManifiesto_CoincideConLaCasillaImpresa(FurTemplateFormat format, string id)
    {
        var f = FurFieldManifestLoader.LoadEmbedded(format).Fields.Single(x => x.Id == id);
        var (cx, cy) = Medido(format, id);
        var size = f.Size > 0 ? f.Size : FurCheckboxLayout.DefaultBoxSize;

        (f.X + size / 2).Should().BeApproximately(cx, Tol, $"{format}/{id}: el centro X del manifiesto no coincide con la casilla impresa");
        (f.Y + size / 2).Should().BeApproximately(cy, Tol, $"{format}/{id}: el centro Y del manifiesto no coincide con la casilla impresa");
    }

    [Theory]
    [MemberData(nameof(Casillas))]
    public void CentroDeLaXUnica_CoincideConLaCasillaImpresa(FurTemplateFormat format, string id)
    {
        var f = FurFieldManifestLoader.LoadEmbedded(format).Fields.Single(x => x.Id == id);
        var (cx, cy) = Medido(format, id);

        var p = FurCheckboxLayout.Place(f.X, f.Y, f.Size, 1, f.FontSize).Single();
        var gx = p.LeftX + FurCheckboxLayout.GlyphWidthRatio * p.FontSize / 2;
        var gy = p.BaselineY - FurCheckboxLayout.CapHeightRatio * p.FontSize / 2;

        gx.Should().BeApproximately(cx, Tol, $"{format}/{id}: la X dibujada no queda centrada en horizontal en la casilla impresa");
        gy.Should().BeApproximately(cy, Tol, $"{format}/{id}: la X dibujada no queda centrada en vertical en la casilla impresa");
    }

    [Fact]
    public void ElJson_NoTrae_IdsQueNoEstenEnElManifiesto_Y_SumaElTotalEsperado()
    {
        var total = 0;
        foreach (var format in Formatos)
        {
            var ids = FurFieldManifestLoader.LoadEmbedded(format).Fields
                .Where(f => f.Type == FurFieldType.Checkbox).Select(f => f.Id).ToHashSet();
            var json = Centros.Value[format.ToString()].Keys;
            json.Except(ids).Should().BeEmpty($"{format}: el JSON trae ids que no son casillas del manifiesto");
            total += json.Count;
        }
        total.Should().Be(TotalEsperado);
    }
}
