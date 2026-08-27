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
    [InlineData(null, true)]
    [InlineData("AUTO", true)]
    [InlineData("OPERATOR_CHOICE", true)]
    [InlineData("operator_choice", true)]
    [InlineData("MANUAL", false)]
    [InlineData("manual", false)]
    public void AllowsAutomaticImpronta_SoloManualLaApaga(string? source, bool esperado)
    {
        var json = source is null ? "{}" : $$"""{"improntaSource":"{{source}}"}""";
        ProcedureTypeGateProfile.FromJson(json).AllowsAutomaticImpronta().Should().Be(esperado);
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

    // ── AdmiteDimensionDePrenda ──────────────────────────────────────────────────────────
    // Predicado ÚNICO de «este expediente puede tener una decisión de prenda», compartido por el gate
    // de preparación, el blocker del asistente y RegistrarPrendaHandler.

    [Theory]
    [InlineData("MATRICULAS")]
    [InlineData("TRASPASO")]
    public void AdmiteDimensionDePrenda_FamiliasQueAcumulan_Admiten(string familia)
    {
        // La prenda del art. 5.1.8 se añade por encima del tipo base: sigue admitida.
        ProcedureTypeGateProfile.FromJson("{}")
            .AdmiteDimensionDePrenda(familia, "MATRICULA_INICIAL")
            .Should().BeTrue();
    }

    [Theory]
    [InlineData("DUPLICADO_TARJETA")]
    [InlineData("CAMBIO_COLOR")]
    [InlineData("CAMBIO_CARROCERIA")]
    [InlineData("CANCELACION_MATRICULA")]
    public void AdmiteDimensionDePrenda_OtrosNoPrendario_NoAdmite(string tipo)
    {
        // El caso que motivó el predicado: en OTROS el cambio ES el trámite (ADR-0050), así que no
        // hay gravamen que decidir — y por tanto tampoco por el que bloquear al preparar.
        ProcedureTypeGateProfile.FromJson("{}")
            .AdmiteDimensionDePrenda("OTROS", tipo)
            .Should().BeFalse();
    }

    [Theory]
    [InlineData("PRENDA_INSCRIPCION")]
    [InlineData("LEVANTAMIENTO_PRENDA")]
    [InlineData("LEVANTAR_INSCRIBIR_PRENDA")]
    [InlineData("CAMBIO_ACREEDOR")]
    public void AdmiteDimensionDePrenda_TipoPrendarioDeOtros_SiAdmite(string tipo)
    {
        // Estos viven en OTROS pero la prenda ES el trámite: conservan su gate íntegro. Acotar el
        // override no puede dejar sin decisión de prenda justo a los trámites de prenda.
        ProcedureTypeGateProfile.FromJson("{}")
            .AdmiteDimensionDePrenda("OTROS", tipo)
            .Should().BeTrue();
    }

    [Fact]
    public void AdmiteDimensionDePrenda_PerfilQueLaDeclara_GanaALaFamilia()
    {
        // Precedencia perfil → familia, igual que ComplementaryPrendaAllowed: un tipo de OTROS que
        // declare explícitamente el gravamen complementario vuelve a admitirlo sin tocar código.
        ProcedureTypeGateProfile.FromJson("""{"allowsComplementaryPrenda":true}""")
            .AdmiteDimensionDePrenda("OTROS", "CAMBIO_COLOR")
            .Should().BeTrue();
    }

    [Fact]
    public void AdmiteDimensionDePrenda_SinFamiliaNiTipo_Admite()
    {
        // Fail-safe deliberado: un expediente cuyo tipo llegue sin clasificar conserva el
        // comportamiento previo (gate activo) en vez de perder la prenda en silencio.
        ProcedureTypeGateProfile.FromJson("{}")
            .AdmiteDimensionDePrenda(null, null)
            .Should().BeTrue();
    }
}