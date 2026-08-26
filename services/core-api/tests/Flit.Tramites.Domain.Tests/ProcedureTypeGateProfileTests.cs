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

    [Fact]
    public void FromJson_ParsesRequiresPlateRequest()
    {
        // FEATURE-08 / HU-BE-05 (CFD-08).
        var profile = ProcedureTypeGateProfile.FromJson("{ \"requiresPlateRequest\": true }");
        profile.RequiresPlateRequest.Should().BeTrue();
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

    // ── Quién elige el organismo de tránsito ─────────────────────────────────────────────────

    [Theory]
    [InlineData("OPERATOR", true)]
    [InlineData("operator", true)]
    [InlineData("RUNT", false)]
    [InlineData("runt", false)]
    public void OperatorChoosesTransitOffice_LoDeclaradoManda(string fuente, bool esperado)
    {
        // Un radicado de cuenta entra por PLACA y aun así lo elige el operador: deducirlo del
        // identificador no puede describir ese caso, por eso se declara.
        var perfil = ProcedureTypeGateProfile.FromJson(
            $$"""{"entryMode":"PLATE","transitOfficeSource":"{{fuente}}"}""");

        perfil.OperatorChoosesTransitOffice().Should().Be(esperado);
    }

    [Theory]
    [InlineData("VIN", true)]
    [InlineData("PLATE", false)]
    [InlineData(null, false)]
    public void OperatorChoosesTransitOffice_SinDeclarar_CaeAlModoDeEntrada(string? entryMode, bool esperado)
    {
        // Ausente NO es RUNT: es el criterio anterior a la llave, para que los veinte tipos
        // restantes y los snapshots ya congelados se comporten exactamente igual que antes.
        var json = entryMode is null ? "{}" : $$"""{"entryMode":"{{entryMode}}"}""";

        ProcedureTypeGateProfile.FromJson(json).OperatorChoosesTransitOffice().Should().Be(esperado);
    }
}