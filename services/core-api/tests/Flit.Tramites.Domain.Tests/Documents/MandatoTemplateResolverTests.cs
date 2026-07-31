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
}
