using Flit.Tramites.Domain.Tramites.Services;
using FluentAssertions;
using Xunit;

namespace Flit.Tramites.Domain.Tests;

/// <summary>
/// FEATURE-08 / HU-BE-02 (CFD-02) — vista tipada del <c>gate_profile</c>. Deserialización tolerante
/// y catálogo de entryMode (PLATE/VIN/BOTH).
/// </summary>
public sealed class ProcedureTypeGateProfileTests
{
    [Fact]
    public void FromJson_ParsesEntryModeAndValidationFlags()
    {
        var json = """
        { "entryMode": "BOTH", "requiresBuyer": true, "validateCompanyRule": true,
          "validateOtOperability": true, "validateDuplicateProcedure": true,
          "biometricActors": ["BUYER","OWNER"] }
        """;

        var profile = ProcedureTypeGateProfile.FromJson(json);

        profile.EntryMode.Should().Be("BOTH");
        profile.RequiresBuyer.Should().BeTrue();
        profile.ValidateCompanyRule.Should().BeTrue();
        profile.ValidateOtOperability.Should().BeTrue();
        profile.ValidateDuplicateProcedure.Should().BeTrue();
        profile.BiometricActors.Should().ContainInOrder("BUYER", "OWNER");
        profile.RequiresInitialValidation.Should().BeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-json")]
    [InlineData("{ malformed ")]
    public void FromJson_NullEmptyOrCorrupt_ReturnsDefaultProfile(string? json)
    {
        var profile = ProcedureTypeGateProfile.FromJson(json);

        profile.EntryMode.Should().BeNull();
        profile.ValidateCompanyRule.Should().BeFalse();
        profile.ValidateOtOperability.Should().BeFalse();
        profile.ValidateDuplicateProcedure.Should().BeFalse();
        profile.RequiresInitialValidation.Should().BeFalse();
        profile.BiometricActors.Should().BeEmpty();
    }

    [Fact]
    public void FromJson_IsCaseInsensitiveOnKeys()
    {
        var profile = ProcedureTypeGateProfile.FromJson("{ \"EntryMode\": \"VIN\" }");
        profile.EntryMode.Should().Be("VIN");
    }

    [Fact]
    public void FromJson_ParsesCommercialBiometricAndSignatureFlags()
    {
        // FEATURE-08 / HU-BE-04 (CFD-06/CFD-07): flags comercial/identidad/firma en gate_profile.
        var json = """
        { "requiresCommercialValue": true, "commercialValueSource": "FASECOLDA",
          "requiresBiometrics": true, "biometricActors": ["BUYER","OWNER"],
          "requiresSignature": true }
        """;

        var profile = ProcedureTypeGateProfile.FromJson(json);

        profile.RequiresCommercialValue.Should().BeTrue();
        profile.CommercialValueSource.Should().Be("FASECOLDA");
        profile.RequiresBiometrics.Should().BeTrue();
        profile.BiometricActors.Should().ContainInOrder("BUYER", "OWNER");
        profile.RequiresSignature.Should().BeTrue();
    }

    [Theory]
    [InlineData("PLATE", true)]
    [InlineData("VIN", true)]
    [InlineData("BOTH", true)]
    [InlineData("plate", false)] // catálogo en mayúsculas
    [InlineData("UNKNOWN", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsValidEntryMode_AcceptsOnlyCatalog(string? value, bool valid)
    {
        ProcedureTypeGateProfile.IsValidEntryMode(value).Should().Be(valid);
    }
}
