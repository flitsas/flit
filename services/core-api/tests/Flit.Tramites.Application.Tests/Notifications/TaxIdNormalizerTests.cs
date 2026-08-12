using Flit.Tramites.Application.Notifications;
using FluentAssertions;
using Xunit;

namespace Flit.Tramites.Application.Tests.Notifications;

/// <summary>HU #11486 — normalización de NIT para marca Renting (AC3).</summary>
public sealed class TaxIdNormalizerTests
{
    [Theory]
    [InlineData("811011779", "811011779")]
    [InlineData("811011779-1", "811011779")]
    [InlineData("811.011.779", "811011779")]
    [InlineData("811.011.779-1", "811011779")]
    [InlineData("8110117791", "811011779")]
    public void RentingNitVariants_NormalizanALaBase(string input, string expected) =>
        TaxIdNormalizer.NormalizeBase(input).Should().Be(expected);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("abc")]
    public void EntradaInvalida_DevuelveVacio(string? input) =>
        TaxIdNormalizer.NormalizeBase(input).Should().BeEmpty();

    [Fact]
    public void OtroNit_NoCoincideConRenting() =>
        TaxIdNormalizer.NormalizeBase("900123456-7").Should().Be("900123456");
}
