using Flit.Admin.Application.Companies.MandateSigners;
using Flit.Admin.Domain.Companies.MandateSigners;
using FluentAssertions;
using Xunit;

namespace Flit.Admin.Tests.Companies.MandateSigners;

/// <summary>
/// HU #11715 — no se habilita en un organismo a un mandatario que no puede firmar ante él. La regla
/// vive al parametrizar, no al emitir, y replica la precedencia de <c>MandatarioFirmaResolver</c>.
/// </summary>
public sealed class MandateSignerSigningCapabilityTests
{
    private static readonly Guid Funza = Guid.Parse("eeacc872-a522-56bb-9150-70776b094009");
    private static readonly Guid Bogota = Guid.Parse("aaaaaaaa-0001-4000-8000-000000000001");
    private static readonly Guid Firma = Guid.Parse("11111111-2222-3333-4444-555555555555");

    private static MandateSignerItem Signer(
        string identityStatus = "none",
        Guid? signatureVaultId = null,
        string? email = null,
        IReadOnlyList<Guid>? transitOfficeIds = null) =>
        new()
        {
            Id = Guid.NewGuid(),
            FullName = "MANDATARIO DE PRUEBA",
            DocumentNumber = "1099999999",
            IdentityStatus = identityStatus,
            SignatureVaultId = signatureVaultId,
            Email = email,
            TransitOfficeIds = transitOfficeIds ?? [],
            IsActive = true,
        };

    [Fact]
    public void SinFirmaNiIdentidadNiCorreo_NoSePuedeHabilitar()
    {
        // AC1 — es el caso que dejaba el contrato con la línea en blanco sin avisar.
        var error = MandateSignerSigningCapability.Validate([Funza], null, null);

        error.Should().NotBeNull();
        error!.Field.Should().Be("transitOfficeIds");
        error.Message.Should().Be(MandateSignerSigningCapability.SinMedioDeFirmaMessage);
    }

    [Fact]
    public void ConFirmaDelBaul_SeHabilita()
    {
        // AC2.
        MandateSignerSigningCapability.Validate([Funza, Bogota], null, Firma).Should().BeNull();
    }

    [Fact]
    public void ConIdentidadVigente_SeHabilita()
    {
        // AC3.
        MandateSignerSigningCapability
            .Validate([Funza], null, null, Signer(identityStatus: "valid"))
            .Should().BeNull();
    }

    [Fact]
    public void SoloConCorreo_NoAlcanza()
    {
        // HU #13122 AC1/AC4 — el correo ya no cuenta: un mandatario que solo tiene correo (sin baúl ni
        // validación biométrica) no puede firmar y se rechaza, tanto en el alta como al editarlo.
        var soloCorreo = Signer(email: "mandatario@ejemplo.com");

        var enEdicion = MandateSignerSigningCapability.Validate([Funza], null, null, soloCorreo);
        var enAlta = MandateSignerSigningCapability.Validate([Funza], null, null);

        enEdicion.Should().NotBeNull();
        enEdicion!.Message.Should().Be(MandateSignerSigningCapability.SinMedioDeFirmaMessage);
        enAlta.Should().NotBeNull();
    }

    [Fact]
    public void ElMensaje_NoSugiereElCorreo_YIndicaBaulOBiometria()
    {
        var mensaje = MandateSignerSigningCapability.SinMedioDeFirmaMessage;

        mensaje.Should().NotContainEquivalentOf("correo");
        mensaje.Should().Contain("baúl").And.Contain("biométrica");
    }

    [Fact]
    public void IdentidadEnCurso_SeHabilita()
    {
        // HU #13122 AC3 — una validación biométrica pendiente cuenta como medio en camino.
        MandateSignerSigningCapability
            .Validate([Funza], null, null, Signer(identityStatus: "pending"))
            .Should().BeNull();
    }

    [Fact]
    public void IdentidadVencida_NoAlcanza()
    {
        // Una validación vencida no estampa sello; renovarla es una acción explícita del gestor.
        MandateSignerSigningCapability
            .Validate([Funza], null, null, Signer(identityStatus: "expired"))
            .Should().NotBeNull();
    }

    [Fact]
    public void FirmaFisica_QuedaExenta()
    {
        // Firma a mano no exige baúl al parametrizar. Si más adelante hay imagen o sello, el contrato
        // sí la estampa: el modelo a mano ya no oculta una firma que el mandatario tiene.
        MandateSignerSigningCapability.Validate([Funza], [Funza], null).Should().BeNull();
    }

    [Fact]
    public void FirmaFisica_SoloExentaElOrganismoMarcado()
    {
        var sinFirma = MandateSignerSigningCapability.OrganismosSinMedioDeFirma(
            [Funza, Bogota], [Funza], null);

        sinFirma.Should().ContainSingle().Which.Should().Be(Bogota);
    }

    [Fact]
    public void SinOrganismosNuevos_NoSeValidaNada()
    {
        // AC6 — editar un mandatario que ya incumple no obliga a arreglarlo.
        MandateSignerSigningCapability.Validate([], null, null, Signer()).Should().BeNull();
    }

    [Fact]
    public void LaFirmaYaGuardadaCuenta_SinReenviarlaEnCadaGuardado()
    {
        MandateSignerSigningCapability
            .Validate([Funza], null, null, Signer(signatureVaultId: Firma))
            .Should().BeNull();
    }
}
