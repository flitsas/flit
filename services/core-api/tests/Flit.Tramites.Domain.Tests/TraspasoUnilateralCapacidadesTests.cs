using Flit.Tramites.Domain.Tramites.Services;
using FluentAssertions;
using Xunit;

namespace Flit.Tramites.Domain.Tests;

/// <summary>
/// ADR-0051 — las cuatro capacidades declaradas que separan <c>TRASPASO_UNILATERAL</c> de
/// <c>TRASPASO_STANDARD</c>. Lo que se fija aquí es la PRECEDENCIA de cada llave: lo declarado
/// manda, y su ausencia reproduce exactamente el comportamiento anterior al ADR. Sin esa segunda
/// mitad, cualquier perfil sembrado antes de estas llaves —o el snapshot congelado de un borrador
/// en curso— cambiaría de conducta en silencio.
/// </summary>
public sealed class TraspasoUnilateralCapacidadesTests
{
    /// <summary>El seed real de `94-traspaso-unilateral-capacidades-declaradas.sql`.</summary>
    private const string SeedUnilateral = """
    { "entryMode": "PLATE", "requiresBuyer": true, "requiresSeller": true,
      "sellerCapturedViaForm": false, "signatureActors": ["OWNER"],
      "requiresBiometrics": true, "biometricActors": ["OWNER"],
      "generatesSaleDocument": false, "hasAppraisalBlock": false,
      "requiresSignature": true, "validateOtOperability": true, "simitMode": "INTERNAL" }
    """;

    /// <summary>El de `TRASPASO_STANDARD`, que no declara ninguna de las llaves nuevas.</summary>
    private const string SeedStandard = """
    { "entryMode": "PLATE", "requiresSeller": true, "requiresBuyer": true,
      "requiresCommercialValue": true, "requiresBiometrics": true,
      "biometricActors": ["OWNER","BUYER"], "requiresSignature": true }
    """;

    [Fact]
    public void Unilateral_TieneParteVendedoraPeroNoLaCapturaPorFormulario()
    {
        var profile = ProcedureTypeGateProfile.FromJson(SeedUnilateral);

        // Las dos preguntas que antes respondía una sola llave: el propietario EXISTE en el FUR…
        profile.RequiresSeller.Should().BeTrue();
        // …pero no llega tecleado en el wizard, sino sincronizado desde RUES/RUNT.
        profile.SellerCapturedViaForm.Should().BeFalse();
    }

    [Fact]
    public void SellerCapturedViaForm_AusenteEsTrue_ElComportamientoPrevio()
    {
        // Todo tipo que hoy exige vendedor lo captura por formulario; la llave nueva no puede
        // apagarle el formulario a un traspaso estándar por el mero hecho de no declararla.
        ProcedureTypeGateProfile.FromJson(SeedStandard).SellerCapturedViaForm.Should().BeTrue();
        ProcedureTypeGateProfile.FromJson("{}").SellerCapturedViaForm.Should().BeTrue();
    }

    [Fact]
    public void ResolveSignatureActors_LoDeclaradoManda()
    {
        // En unilateral firma SOLO el propietario: el locatario no comparece como comprador firmante.
        ProcedureTypeGateProfile.FromJson(SeedUnilateral)
            .ResolveSignatureActors().Should().ContainSingle().Which.Should().Be("OWNER");
    }

    [Fact]
    public void ResolveSignatureActors_SinDeclarar_CaeAlCriterioPrevio()
    {
        // Sin la llave, el resolutor reproduce el ternario que ADR-0051 vino a borrar de FurCommand.
        ProcedureTypeGateProfile.FromJson(SeedStandard)
            .ResolveSignatureActors().Should().BeEquivalentTo("OWNER", "BUYER");

        ProcedureTypeGateProfile.FromJson("""{ "requiresSeller": false }""")
            .ResolveSignatureActors().Should().ContainSingle().Which.Should().Be("BUYER");
    }

    [Fact]
    public void ResolveSignatureActors_ArregloVacio_NoDejaAlTramiteSinFirmantes()
    {
        // El JSON no distingue "llave ausente" de "arreglo vacío", y ningún tipo real declara cero
        // firmantes: leer el vacío al pie de la letra dejaría un FUR sin un solo sello.
        ProcedureTypeGateProfile.FromJson("""{ "requiresSeller": true, "signatureActors": [] }""")
            .ResolveSignatureActors().Should().BeEquivalentTo("OWNER", "BUYER");
    }

    [Fact]
    public void GeneratesSaleDocument_UnilateralNoAutogeneraCompraventa()
    {
        // Es de familia TRASPASO y con parte vendedora: por el criterio anterior (ADR-0035, "siempre
        // en traspaso") habría generado compraventa. El locatario ya tenía el vehículo por el
        // contrato de leasing, así que no hay compraventa entre dos partes que generar.
        ProcedureTypeGateProfile.FromJson(SeedUnilateral)
            .GeneratesSaleDocumentAllowed("TRASPASO").Should().BeFalse();
    }

    [Fact]
    public void GeneratesSaleDocument_SinDeclarar_SigueDecidiendoVendedorMasFamilia()
    {
        var standard = ProcedureTypeGateProfile.FromJson(SeedStandard);

        standard.GeneratesSaleDocumentAllowed("TRASPASO").Should().BeTrue();
        // La misma combinación fuera de la familia traspaso no autogenera nada (matrícula, OTROS).
        standard.GeneratesSaleDocumentAllowed("MATRICULAS").Should().BeFalse();
        ProcedureTypeGateProfile.FromJson("{}").GeneratesSaleDocumentAllowed("TRASPASO").Should().BeFalse();
    }

    [Fact]
    public void HasAppraisalBlock_UnilateralNoImprimeAvaluo_YSinDeclararloDecideLaFamilia()
    {
        ProcedureTypeGateProfile.FromJson(SeedUnilateral)
            .HasAppraisalBlockAllowed("TRASPASO").Should().BeFalse();
        ProcedureTypeGateProfile.FromJson(SeedStandard)
            .HasAppraisalBlockAllowed("TRASPASO").Should().BeTrue();
    }

    [Fact]
    public void BiometricActors_UnilateralValidaIdentidadDelPropietario_NoDelComprador()
    {
        // El seed anterior a este ADR traía ["BUYER"], que era justo lo contrario de lo validado.
        ProcedureTypeGateProfile.FromJson(SeedUnilateral)
            .BiometricActors.Should().ContainSingle().Which.Should().Be("OWNER");
    }
}
