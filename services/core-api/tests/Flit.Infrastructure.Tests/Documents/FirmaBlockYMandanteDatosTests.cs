using Flit.Infrastructure.Documents;
using Flit.Tramites.Application.Documents;
using FluentAssertions;
using Xunit;

namespace Flit.Infrastructure.Tests.Documents;

/// <summary>
/// HU #11046 y #11047 — las dos decisiones del bloque de firma que se pueden verificar sin leer el PDF:
/// <b>qué se estampa</b> sobre la línea (prioridad del baúl, HU #11031) y <b>qué datos</b> van debajo,
/// en el orden que pidió el negocio. La composición gráfica (estampa sobre la línea) se comprueba con la
/// herramienta de render de <c>artifacts/render-documentos</c>, igual que el resto de los generadores.
/// </summary>
public sealed class FirmaBlockYMandanteDatosTests
{
    private static readonly byte[] FirmaBaul = [1, 2, 3];
    private static readonly byte[] FirmaIdentidadPng =
        [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private const string Sello = "Validación biométrica de identidad\nCertificado ABC123";

    // ── HU #11046: qué se estampa sobre la línea ──────────────────────────────

    [Fact]
    public void ConFirmaDelBaul_SeEstampaElBaulYNoElSello()
    {
        // La trampa de la HU #11031: con baúl vigente NO se añade además el sello de identidad.
        FlitFirmaBlock.ResolverEstampa(FirmaBaul, Sello).Should().Be(FlitEstampa.Baul);
    }

    [Fact]
    public void ConImagenDeIdentidad_YSinBaul_SeEstampaLaImagen()
    {
        FlitFirmaBlock.ResolverEstampa(null, FirmaIdentidadPng, Sello).Should().Be(FlitEstampa.ImagenIdentidad);
    }

    [Fact]
    public void ConStreamCrudoEnVezDePng_CaeAlSelloDeIdentidad()
    {
        FlitFirmaBlock.ResolverEstampa(null, [9, 8, 7], Sello).Should().Be(FlitEstampa.SelloIdentidad);
    }

    [Fact]
    public void ConBaulEImagenDeIdentidad_GanaElBaul()
    {
        FlitFirmaBlock.ResolverEstampa(FirmaBaul, FirmaIdentidadPng, Sello).Should().Be(FlitEstampa.Baul);
    }

    [Fact]
    public void SinBaulNiSello_NoSeEstampaNada_YLaLineaQuedaParaFirmaManuscrita()
    {
        FlitFirmaBlock.ResolverEstampa(null, null).Should().Be(FlitEstampa.Ninguna);
        FlitFirmaBlock.ResolverEstampa([], "   ").Should().Be(FlitEstampa.Ninguna);
    }

    // ── HU #11047: datos del MANDANTE bajo la línea ───────────────────────────

    private static DocumentParte Juridica() =>
        new(
            "vendedor", "BANCOLOMBIA S.A.S", "890903938", "daniel.amado@flitsas.com", "NIT", "3112789718",
            EsJuridica: true,
            RepresentanteLegalNombre: "Juan Felipe Montoya",
            RepresentanteLegalTipoDoc: "CC",
            RepresentanteLegalDocumento: "1038409485");

    private static DocumentParte Natural() =>
        new("vendedor", "Juan Pérez", "123456", "juan@x.com", "CC", "3001112233");

    [Fact]
    public void PersonaJuridica_ImprimeEmpresaNitNombreDocumentoCelularYCorreo_EnEseOrden()
    {
        var lineas = MandatoPdfGenerator.MandanteIdentificacion(Juridica(), esJuridica: true).ToList();

        lineas.Should().Equal(
            "EMPRESA: BANCOLOMBIA S.A.S",
            "NIT: 890903938",
            "NOMBRE: Juan Felipe Montoya",
            "CÉDULA DE CIUDADANÍA: 1038409485",
            "CELULAR: 3112789718",
            "CORREO ELECTRÓNICO: daniel.amado@flitsas.com");
    }

    [Fact]
    public void PersonaNatural_NoImprimeEmpresaNiNit_YSiElContacto()
    {
        var lineas = MandatoPdfGenerator.MandanteIdentificacion(Natural(), esJuridica: false).ToList();

        lineas.Should().Equal(
            "NOMBRE: Juan Pérez",
            "CÉDULA DE CIUDADANÍA: 123456",
            "CELULAR: 3001112233",
            "CORREO ELECTRÓNICO: juan@x.com");
        lineas.Should().NotContain(l => l.StartsWith("EMPRESA", StringComparison.Ordinal));
        lineas.Should().NotContain(l => l.StartsWith("NIT", StringComparison.Ordinal));
    }

    [Fact]
    public void ContactoAusente_ImprimeElMarcadorYNoAlteraElRestoDelBloque()
    {
        var sinContacto = new DocumentParte("vendedor", "Juan Pérez", "123456", null, "CC", null);

        var lineas = MandatoPdfGenerator.MandanteIdentificacion(sinContacto, esJuridica: false).ToList();

        lineas.Should().Equal(
            "NOMBRE: Juan Pérez",
            "CÉDULA DE CIUDADANÍA: 123456",
            "CELULAR: ___",
            "CORREO ELECTRÓNICO: ___");
    }

    [Fact]
    public void PersonaJuridica_ElNombreYDocumentoSonLosDelRepresentanteLegal()
    {
        var lineas = MandatoPdfGenerator.MandanteIdentificacion(Juridica(), esJuridica: true).ToList();

        // Quien firma el mandato de una empresa es su representante legal, no la empresa.
        lineas.Should().Contain("NOMBRE: Juan Felipe Montoya");
        lineas.Should().NotContain("NOMBRE: BANCOLOMBIA S.A.S");
    }

    [Fact]
    public void Mandatario_LlevaNombreYCedulaEtiquetadosIgualQueElMandante()
    {
        var lineas = MandatoPdfGenerator
            .MandatarioIdentificacion(new MandatarioFirmante("Carlos Ruiz", "70111222"))
            .ToList();

        lineas.Should().Equal("NOMBRE: Carlos Ruiz", "CÉDULA DE CIUDADANÍA: 70111222");
    }

    [Fact]
    public void MandatarioSinResolver_UsaMarcadorSinRomperElBloque()
    {
        var lineas = MandatoPdfGenerator.MandatarioIdentificacion(null).ToList();

        lineas.Should().Equal("NOMBRE: ___", "CÉDULA DE CIUDADANÍA: ___");
    }
}
