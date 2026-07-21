using Flit.Tramites.Domain.Tramites.Services;
using FluentAssertions;
using Xunit;

namespace Flit.Tramites.Domain.Tests;

/// <summary>
/// FEATURE-08 / HU-BE-03 (CFD-05) — vista tipada del <c>validation_profile</c> de un actor.
/// Cubre BE-03-AC-05 (deserializa el perfil de LESSEE con sus flags).
/// </summary>
public sealed class ConformationRuleProfileTests
{
    [Fact]
    public void FromJson_ParsesLesseeProfileFlags()
    {
        // BE-03-AC-05: LESSEE persona jurídica que requiere RUNT.
        var json = """
        { "allowsNaturalPerson": false, "allowsJuridicalPerson": true,
          "allowsMultiple": false, "requiresRunt": true, "requiresSimit": false }
        """;

        var profile = ConformationRuleProfile.FromJson(json);

        profile.AllowsNaturalPerson.Should().BeFalse();
        profile.AllowsJuridicalPerson.Should().BeTrue();
        profile.AllowsMultiple.Should().BeFalse();
        profile.RequiresRunt.Should().BeTrue();
        profile.RequiresSimit.Should().BeFalse();
    }

    [Fact]
    public void FromJson_ParsesEntryModeForVehicleRule()
    {
        var profile = ConformationRuleProfile.FromJson("{ \"entryMode\": \"PLATE\" }");
        profile.EntryMode.Should().Be("PLATE");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-json")]
    public void FromJson_NullEmptyOrCorrupt_ReturnsDefault(string? json)
    {
        var profile = ConformationRuleProfile.FromJson(json);

        profile.AllowsNaturalPerson.Should().BeFalse();
        profile.AllowsJuridicalPerson.Should().BeFalse();
        profile.RequiresRunt.Should().BeFalse();
        profile.EntryMode.Should().BeNull();
    }
}
