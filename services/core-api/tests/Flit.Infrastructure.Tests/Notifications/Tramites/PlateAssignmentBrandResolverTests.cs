using Flit.Infrastructure.Notifications.Tramites;
using Flit.Tramites.Application.Notifications;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Flit.Infrastructure.Tests.Notifications.Tramites;

/// <summary>HU #11486 — marca FLIT/Renting por NIT (AC3, AC4).</summary>
public sealed class PlateAssignmentBrandResolverTests
{
    private static PlateAssignmentBrandResolver CreateSut() =>
        new(null!, null!, NullLogger<PlateAssignmentBrandResolver>.Instance);

    [Theory]
    [InlineData("811011779", PlateAssignmentEmailBrand.Renting)]
    [InlineData("811011779-1", PlateAssignmentEmailBrand.Renting)]
    [InlineData("811.011.779", PlateAssignmentEmailBrand.Renting)]
    public void NitRenting_ResuelveMarcaRenting(string taxId, PlateAssignmentEmailBrand expected) =>
        CreateSut().ResolveFromTaxId(taxId).Should().Be(expected);

    [Theory]
    [InlineData("900123456-7")]
    [InlineData("123456789")]
    [InlineData(null)]
    [InlineData("")]
    public void OtroNit_ResuelveMarcaFlit(string? taxId) =>
        CreateSut().ResolveFromTaxId(taxId).Should().Be(PlateAssignmentEmailBrand.Flit);
}
