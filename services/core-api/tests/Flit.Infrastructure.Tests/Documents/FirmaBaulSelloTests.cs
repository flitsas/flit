using Flit.Infrastructure.Documents;
using Flit.Tramites.Application.Documents;
using FluentAssertions;
using Xunit;

namespace Flit.Infrastructure.Tests.Documents;

/// <summary>
/// HU #11170 — la trazabilidad de la firma del baúl (vigencia y hash) deja de ser exclusiva del FUR.
/// Aquí se verifica el TEXTO del sello, que es la decisión comprobable sin leer el PDF; que cada
/// generador lo pinte en su sitio se comprueba con <c>artifacts/render-documentos</c>, igual que el
/// resto de los documentos.
/// </summary>
public sealed class FirmaBaulSelloTests
{
    private static readonly Guid VaultId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");

    private static FirmaBaulMetadata Meta(string? hash = "ABC-123-XYZ") =>
        new("900123456", "RENTING SAS", new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), VaultId, hash);

    [Fact]
    public void SinIdentificacion_SoloVigenciaYHash()
    {
        // Compraventa, mandato y solicitud virtual ya imprimen nombre y documento bajo la línea de
        // firma: repetirlos sería ruido y robaría alto al bloque (HU #11034).
        var sello = FlitFirmaBaulSello.Build(Meta(), incluirIdentificacion: false);

        sello.Split('\n').Should().Equal("Vig. 2026/01/01 — 2026/12/31", "Hash: ABC-123-XYZ");
    }

    [Fact]
    public void ConIdentificacion_AntepondeDocumentoYNombre_ComoEnElFur()
    {
        // El espacio de firma del FUR no identifica al firmante en ninguna otra parte.
        var sello = FlitFirmaBaulSello.Build(Meta(), incluirIdentificacion: true);

        sello.Split('\n').Should().Equal(
            "Doc. 900123456",
            "RENTING SAS",
            "Vig. 2026/01/01 — 2026/12/31",
            "Hash: ABC-123-XYZ");
    }

    [Fact]
    public void SinCodigoHash_SeOmiteLaLinea_YNuncaSeImprimeElUuid()
    {
        // HU #10930: el GUID de la fila no es el hash y confundía al operador.
        var sello = FlitFirmaBaulSello.Build(Meta(hash: null), incluirIdentificacion: false);

        sello.Should().NotContain("Hash:");
        sello.Should().NotContain(VaultId.ToString("D"));
        sello.Should().Be("Vig. 2026/01/01 — 2026/12/31");
    }

    [Fact]
    public void ParteSinFirmaDelBaul_NoLlevaSello()
    {
        var metadatos = new Dictionary<string, FirmaBaulMetadata> { ["comprador"] = Meta() };

        FlitFirmaBaulSello.Resolve(metadatos, "vendedor", incluirIdentificacion: false).Should().BeNull();
        FlitFirmaBaulSello.Resolve(null, "comprador", incluirIdentificacion: false).Should().BeNull();
        FlitFirmaBaulSello.Resolve(metadatos, null, incluirIdentificacion: false).Should().BeNull();
    }

    [Theory]
    [InlineData("vendedor")]
    [InlineData("VENDEDOR")]
    [InlineData("Vendedor actual")]
    public void ElSelloSeResuelveConLosMismosAliasQueLaImagen(string rol)
    {
        // Si los alias divergieran, una parte podría quedar con la firma estampada y sin vigencia ni
        // hash: la imagen se resuelve por "vendedor" y el sello no la encontraría.
        var metadatos = new Dictionary<string, FirmaBaulMetadata>(StringComparer.OrdinalIgnoreCase)
        {
            ["vendedor"] = Meta(),
        };

        FlitFirmaBaulSello.Resolve(metadatos, rol, incluirIdentificacion: false)
            .Should().Contain("Hash: ABC-123-XYZ");
    }
}
