using Flit.Tramites.Application.Documents;
using FluentAssertions;
using Xunit;

namespace Flit.Tramites.Application.Tests.Documents;

/// <summary>HU #10919 — normalización y resolución pura clasificación → plantilla de FUR.</summary>
public sealed class FurTemplateResolverTests
{
    [Theory]
    [InlineData("AUTOMOVIL", "AUTOMOVIL")]
    [InlineData("  Camión   Grúa ", "CAMION GRUA")] // tildes + espacios internos + case
    [InlineData("mototraílla", "MOTOTRAILLA")]
    [InlineData(null, "")]
    [InlineData("   ", "")]
    public void Normalize_UpperNoAccentsCollapsed(string? input, string expected) =>
        FurClassificationNormalizer.Normalize(input).Should().Be(expected);

    private static Dictionary<string, FurTemplateFormat> Catalog()
    {
        var pairs = new (string Classification, FurTemplateFormat Format)[]
        {
            ("AUTOMOVIL", FurTemplateFormat.Automotor),
            ("CAMION", FurTemplateFormat.Automotor),
            ("EXCAVADORA", FurTemplateFormat.Maquinaria),
            ("MOTOTRAÍLLA", FurTemplateFormat.Maquinaria),
            ("SEMIREMOLQUE", FurTemplateFormat.Remolques),
            ("MAQ. CONSTRUCCION O MINERA", FurTemplateFormat.Remolques),
        };
        var d = new Dictionary<string, FurTemplateFormat>(StringComparer.Ordinal);
        foreach (var p in pairs)
            d[FurClassificationNormalizer.Normalize(p.Classification)] = p.Format;
        return d;
    }

    [Theory]
    [InlineData("AUTOMOVIL", FurTemplateFormat.Automotor)]
    [InlineData("camion", FurTemplateFormat.Automotor)]
    [InlineData("excavadora", FurTemplateFormat.Maquinaria)]
    [InlineData("Mototraílla", FurTemplateFormat.Maquinaria)] // tilde + case distinto
    [InlineData("SEMIREMOLQUE", FurTemplateFormat.Remolques)]
    [InlineData("Maq. Construccion O Minera", FurTemplateFormat.Remolques)] // literal del CSV → REMOLQUES
    [InlineData("NAVE ESPACIAL", FurTemplateFormat.Automotor)] // sin match → default (D2)
    [InlineData(null, FurTemplateFormat.Automotor)]
    public void Resolve_MapsOrDefaultsAutomotor(string? vehicleClass, FurTemplateFormat expected) =>
        FurTemplateResolution.Resolve(vehicleClass, Catalog()).Should().Be(expected);

    [Fact]
    public void ResolveMatch_ReturnsFieldToFillOrNull()
    {
        var catalog = new Dictionary<string, FurClassificationMatch>(StringComparer.Ordinal)
        {
            [FurClassificationNormalizer.Normalize("CUADRICICLO")] =
                new FurClassificationMatch(FurTemplateFormat.Automotor, "CUATRIMOTO"),
            [FurClassificationNormalizer.Normalize("ALZADORA DE CAÑA")] =
                new FurClassificationMatch(FurTemplateFormat.Maquinaria, "AGRICOLA"),
            [FurClassificationNormalizer.Normalize("MAQ. CONSTRUCCION O MINERA")] =
                new FurClassificationMatch(FurTemplateFormat.Remolques, "SIMILAR"),
        };

        FurTemplateResolution.ResolveMatch("CUADRICICLO", catalog)
            .Should().Be(new FurClassificationMatch(FurTemplateFormat.Automotor, "CUATRIMOTO"));
        FurTemplateResolution.ResolveMatch("alzadora de caña", catalog)
            .Should().Be(new FurClassificationMatch(FurTemplateFormat.Maquinaria, "AGRICOLA"));
        FurTemplateResolution.ResolveMatch("Maq. Construccion O Minera", catalog)
            .Should().Be(new FurClassificationMatch(FurTemplateFormat.Remolques, "SIMILAR"));
        FurTemplateResolution.ResolveMatch("NAVE ESPACIAL", catalog)
            .Should().Be(new FurClassificationMatch(FurTemplateFormat.Automotor, null));
    }

    [Theory]
    [InlineData("AUTOMOTOR", true, FurTemplateFormat.Automotor)]
    [InlineData("maquinaria", true, FurTemplateFormat.Maquinaria)]
    [InlineData(" REMOLQUES ", true, FurTemplateFormat.Remolques)]
    [InlineData("XYZ", false, FurTemplateFormat.Automotor)]
    [InlineData(null, false, FurTemplateFormat.Automotor)]
    public void TryParseFormat_Works(string? raw, bool ok, FurTemplateFormat expected)
    {
        FurTemplateResolution.TryParseFormat(raw, out var f).Should().Be(ok);
        f.Should().Be(expected);
    }
}
