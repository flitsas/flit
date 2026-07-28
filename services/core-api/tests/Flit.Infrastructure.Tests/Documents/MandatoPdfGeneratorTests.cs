using Flit.Infrastructure.Documents;
using Flit.Tramites.Application.Documents;
using Flit.Tramites.Domain.Tramites.Catalog;
using FluentAssertions;
using Xunit;

namespace Flit.Infrastructure.Tests.Documents;

/// <summary>
/// HU #10915 (ADR-0036) — smoke tests del generador del Contrato de Mandato: produce un PDF real (tipo
/// <c>mandato</c>) para las tres variantes (genérica/Sabaneta/Bello) × persona natural/jurídica, oculta
/// las firmas en borrador y no falla sin firmante resuelto (estado preparado) ni sin parte radicadora.
/// (La verificación del contenido textual se hace fuera del test, con render — sin dependencia de un
/// lector de PDF en el proyecto de pruebas, igual que <see cref="SolicitudVirtualPdfGeneratorTests"/>.)
/// </summary>
public sealed class MandatoPdfGeneratorTests
{
    private static readonly MandatoPdfGenerator Generator = new();

    private static FurDocumentData DataWith(
        DocumentParte? parte, bool firmasVisibles = true, string codigo = "MATRICULA_NUEVA") =>
        new(
            ProcedureInstanceId: Guid.NewGuid(),
            ReferenceNumber: "REF-2026-1",
            Modalidad: "matricula",
            TipologiaCodigo: codigo,
            Vehiculo: new VehiculoDatos(null, null, null, null, null, null, null, "VIN123", "ABC123"),
            Organismo: new OrganismoTransito("5631000", "STRIA MOVILIDAD SABANETA", "Sabaneta"),
            Partes: parte is null ? [] : [parte],
            ValorVenta: null,
            Causal: null,
            SellosFirma: [],
            FirmasVisibles: firmasVisibles);

    private static DocumentParte Natural() =>
        new("comprador", "Juan Pérez", "123456", "juan@x.com", "CC", "3001112233");

    private static DocumentParte Juridica() =>
        new(
            "comprador", "Renting S.A.S.", "900123456-7", "info@renting.com", "NIT", "6041112233",
            EsJuridica: true,
            RepresentanteLegalNombre: "Ana Gómez",
            RepresentanteLegalTipoDoc: "CC",
            RepresentanteLegalDocumento: "52123456");

    private static MandatoData Mandato(
        DocumentParte? parte, string template, bool firmasVisibles = true,
        MandatarioFirmante? mandatario = null, string? instName = null, string? instNit = null,
        string codigo = "MATRICULA_NUEVA") =>
        new(DataWith(parte, firmasVisibles, codigo), template, instName, instNit, mandatario);

    [Theory]
    [InlineData("generico")]
    [InlineData("sabaneta")]
    [InlineData("bello")]
    public void ProducesMandatoPdf_ForNaturalAndJuridica(string template)
    {
        var firmante = new MandatarioFirmante("Carlos Ruiz", "70111222");

        var pn = Generator.GenerateMandato(Mandato(Natural(), template, mandatario: firmante));
        pn.Tipo.Should().Be("mandato");
        pn.Mimetype.Should().Be("application/pdf");
        pn.Filename.Should().StartWith("mandato_");
        pn.Content.Should().NotBeEmpty();
        // Cabecera de un PDF válido.
        System.Text.Encoding.ASCII.GetString(pn.Content, 0, 4).Should().Be("%PDF");

        var pj = Generator.GenerateMandato(Mandato(Juridica(), template, mandatario: firmante));
        pj.Content.Should().NotBeEmpty();
    }

    [Fact]
    public void TraspasoTipologia_ProducesMandato()
    {
        var doc = Generator.GenerateMandato(Mandato(
            Juridica(), "generico", mandatario: new MandatarioFirmante("Carlos Ruiz", "70111222"),
            codigo: TramiteTipologiaCatalog.CodigoTraspasoStandard));
        doc.Content.Should().NotBeEmpty();
    }

    [Fact]
    public void HidesSignatures_WhenBorrador_AndDoesNotThrow_WithoutSignerOrRadicador()
    {
        // Preparado/borrador: firmas ocultas y sin mandatario resuelto (placeholders), aún genera el PDF.
        Generator.GenerateMandato(Mandato(Natural(), "generico", firmasVisibles: false))
            .Content.Should().NotBeEmpty();

        // Sin parte radicadora: usa placeholders, no lanza (las tres variantes).
        foreach (var template in new[] { "generico", "sabaneta", "bello" })
            Generator.GenerateMandato(Mandato(null, template)).Content.Should().NotBeEmpty();
    }
}
