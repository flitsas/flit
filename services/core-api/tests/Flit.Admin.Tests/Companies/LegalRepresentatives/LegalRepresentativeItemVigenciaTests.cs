using Flit.Admin.Domain.Companies.LegalRepresentatives;
using Flit.Admin.Domain.Identity;
using FluentAssertions;
using Xunit;

namespace Flit.Admin.Tests.Companies.LegalRepresentatives;

/// <summary>
/// La columna «Firma / Identidad» del listado de representantes refleja la VIGENCIA de hoy, no las
/// referencias guardadas al crear o editar. Un representante con firma vencida en el baúl o identidad
/// vencida/sin validar no puede salir «Con firma o identidad» mientras el detalle dice lo contrario.
/// </summary>
public sealed class LegalRepresentativeItemVigenciaTests
{
    [Fact]
    public void ReferenciasGuardadas_SinVigencia_NoCuentanComoFirmaNiIdentidad()
    {
        var item = new LegalRepresentativeItem
        {
            SignatureVaultId = Guid.NewGuid(),
            IdentityValidationRef = Guid.NewGuid(),
            FirmaBaulVigente = false,
            IdentityStatus = AdminIdentityVigencia.None,
        };

        item.HasSignatureOrIdentity.Should().BeFalse();
    }

    [Theory]
    [InlineData(AdminIdentityVigencia.Expired)]
    [InlineData(AdminIdentityVigencia.Pending)]
    [InlineData(AdminIdentityVigencia.None)]
    public void IdentidadNoVigente_SinFirma_EsSinFirmaNiIdentidad(string identityStatus)
    {
        var item = new LegalRepresentativeItem
        {
            IdentityValidationRef = Guid.NewGuid(),
            FirmaBaulVigente = false,
            IdentityStatus = identityStatus,
        };

        item.HasSignatureOrIdentity.Should().BeFalse();
    }

    [Fact]
    public void FirmaDelBaulVigente_CuentaAunqueNoHayaIdentidad()
    {
        var item = new LegalRepresentativeItem
        {
            FirmaBaulVigente = true,
            IdentityStatus = AdminIdentityVigencia.None,
        };

        item.HasSignatureOrIdentity.Should().BeTrue();
    }

    [Fact]
    public void IdentidadVigente_CuentaAunqueNoHayaFirma()
    {
        var item = new LegalRepresentativeItem
        {
            FirmaBaulVigente = false,
            IdentityStatus = AdminIdentityVigencia.Valid,
        };

        item.HasSignatureOrIdentity.Should().BeTrue();
    }
}
