using Flit.Tramites.Domain.Documents;
using FluentAssertions;
using Xunit;

namespace Flit.Tramites.Domain.Tests.Documents;

/// <summary>
/// HU #10915 (ADR-0036) — el resolver de plantilla de mandato es una función pura y cerrada: mapea el
/// <c>template_code</c> del OT a su variante y cae a <see cref="MandatoVariante.Generico"/> ante cualquier
/// valor desconocido, nulo o vacío (default seguro cuando el OT no tiene configuración).
/// </summary>
public sealed class MandatoTemplateResolverTests
{
    [Theory]
    [InlineData("sabaneta", MandatoVariante.Sabaneta)]
    [InlineData("SABANETA", MandatoVariante.Sabaneta)]
    [InlineData("  bello  ", MandatoVariante.Bello)]
    [InlineData("municipio", MandatoVariante.Municipio)]
    [InlineData("generico", MandatoVariante.Generico)]
    public void Resolve_MapsKnownCodes(string code, MandatoVariante expected) =>
        MandatoTemplateResolver.Resolve(code).Should().Be(expected);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("desconocido")]
    public void Resolve_FallsBackToGenerico(string? code) =>
        MandatoTemplateResolver.Resolve(code).Should().Be(MandatoVariante.Generico);

    [Fact]
    public void Resolve_MapsKnownCodes_DoesNotRemapByPersonType()
    {
        // HU-L10: la emisión usa el template_code del OT; ya no hay ResolveEmissionCode PN/PJ.
        MandatoTemplateResolver.Resolve("sabaneta").Should().Be(MandatoVariante.Sabaneta);
        MandatoTemplateResolver.Resolve("bello").Should().Be(MandatoVariante.Bello);
        MandatoTemplateResolver.Resolve("municipio").Should().Be(MandatoVariante.Municipio);
        MandatoTemplateResolver.Resolve("generico").Should().Be(MandatoVariante.Generico);
    }
}
