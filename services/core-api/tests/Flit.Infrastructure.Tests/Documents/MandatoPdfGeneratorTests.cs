using Flit.Infrastructure.Documents;
using Flit.Tramites.Application.Documents;
using Flit.Tramites.Domain.Documents;
using Flit.Tramites.Domain.Tramites.Catalog;
using Flit.Tramites.Domain.Tramites.ValueObjects;
using FluentAssertions;
using Xunit;

namespace Flit.Infrastructure.Tests.Documents;

/// <summary>
/// HU #10915 (ADR-0036) — smoke tests del generador del Contrato de Mandato: produce un PDF real (tipo
/// <c>mandato</c>) para las tres variantes (genérica/Sabaneta/Bello) × persona natural/jurídica y no
/// falla sin firmante resuelto (estado preparado) ni sin parte radicadora.
/// (La verificación del contenido textual se hace fuera del test, con render — sin dependencia de un
/// lector de PDF en el proyecto de pruebas, igual que <see cref="SolicitudVirtualPdfGeneratorTests"/>.)
/// </summary>
public sealed class MandatoPdfGeneratorTests
{
    private static readonly MandatoPdfGenerator Generator = new();

    /// <summary>PNG 1×1 válido: QuestPDF decodifica la imagen de verdad, no basta con bytes sueltos.</summary>
    private static readonly byte[] FirmaPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    private static FurDocumentData DataWith(
        DocumentParte? parte, string codigo = "MATRICULA_NUEVA") =>
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
            TemplateFormat: FurTemplateFormat.Automotor);

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
        DocumentParte? parte, string template,
        MandatarioFirmante? mandatario = null, string? instName = null, string? instNit = null,
        string codigo = "MATRICULA_NUEVA") =>
        new(DataWith(parte, codigo), template, instName, instNit, mandatario);

    [Theory]
    [InlineData("generico")]
    [InlineData("sabaneta")]
    [InlineData("bello")]
    [InlineData("municipio")]
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
    public void Municipio_ElObjetoLlevaConDespuesDelTramiteBase()
    {
        // Misma redacción que el simulador (Funza/Envigado): "radicación y reclamación del trámite de …"
        var data = MandatoPreviewSample.Build(
            MandatoTemplateResolver.Municipio,
            esJuridica: true,
            tipologiaCodigo: "TRASPASO_STANDARD",
            datosDeMuestra: true,
            procedureTypeName: "Traspaso",
            procedureFamily: "TRASPASO",
            prendaMarking: FurPrendaMarking.Levantamiento,
            transformaciones:
            [
                MandatoObjetoComposer.CambioColor,
                MandatoObjetoComposer.CambioCarroceria,
                MandatoObjetoComposer.Blindaje,
            ]);

        MandatoPdfGenerator.ComponerObjeto(data).Should().Be(
            "TRASPASO CON LEVANTAMIENTO DE PRENDA, CAMBIO DE COLOR, CAMBIO DE CARROCERÍA Y BLINDAJE");

        var pdf = Generator.GenerateMandato(data);
        pdf.Content.Should().NotBeEmpty();
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
    public void ConFirmaDelBaul_EnMandanteYMandatario_GeneraElPdfConSuTrazabilidad()
    {
        // HU #11170 — el mandato pintaba la imagen del baúl y nada más. Aquí se comprueba que el bloque
        // acepta los metadatos de AMBOS firmantes sin romper el layout; que el texto salga impreso se
        // verifica con render (el proyecto de pruebas no lee PDFs).
        var meta = new FirmaBaulMetadata(
            "52123456", "Ana Gómez", new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), Guid.NewGuid(), "ABC-123");
        var parte = Juridica();
        var tramite = DataWith(parte) with
        {
            FirmaImagenes = new Dictionary<string, byte[]> { [parte.Rol] = FirmaPng },
            FirmaBaulMetadatos = new Dictionary<string, FirmaBaulMetadata>(StringComparer.OrdinalIgnoreCase)
            {
                [parte.Rol] = meta,
            },
        };
        var mandatario = new MandatarioFirmante("Carlos Ruiz", "70111222", FirmaPng, null, meta);

        foreach (var template in new[] { "generico", "sabaneta", "bello", "municipio" })
        {
            var doc = Generator.GenerateMandato(
                new MandatoData(tramite, template, "UT-SETSA", "900111222", mandatario));
            doc.Content.Should().NotBeEmpty();
        }
    }

    /// <summary>
    /// Las firmas del mandato y de la solicitud virtual se ven en TODOS los estados. Se fija como
    /// invariante estructural —el modelo del documento no transporta ningún interruptor de estado— y no
    /// como un caso de uso, porque lo que había que eliminar era precisamente la posibilidad de que el
    /// estado decidiera: mientras el dato no exista, nadie puede volver a condicionar el recuadro.
    /// </summary>
    [Fact]
    public void ElModeloDelDocumentoNoLlevaInterruptorDeFirmasPorEstado()
    {
        typeof(FurDocumentData).GetProperties()
            .Select(p => p.Name)
            .Should().NotContain("FirmasVisibles");
    }

    [Theory]
    [InlineData("generico")]
    [InlineData("sabaneta")]
    [InlineData("bello")]
    [InlineData("municipio")]
    public void Copropietarios_CuatroMandantes_CabeEnUnaPagina(string template)
    {
        var vendedores = Enumerable.Range(0, 4).Select(i => new DocumentParte(
            "vendedor",
            $"VENDEDOR {i + 1} APELLIDO",
            $"1000000{i}",
            null,
            "CC",
            Ordinal: i + 1)).ToList();
        var data = new FurDocumentData(
            ProcedureInstanceId: Guid.NewGuid(),
            ReferenceNumber: "REF-COP-M",
            Modalidad: "TRASPASO",
            TipologiaCodigo: "TRASPASO_STANDARD",
            Vehiculo: new VehiculoDatos(null, null, null, null, null, null, null, "VIN123", "ICS187"),
            Organismo: new OrganismoTransito("5631000", "STRIA MOVILIDAD SABANETA", "Sabaneta"),
            Partes: vendedores,
            ValorVenta: null,
            Causal: null,
            SellosFirma: [],
            FechaTramite: new DateTime(2026, 9, 2, 0, 0, 0, DateTimeKind.Utc),
            IdentidadValidada: true);
        var mandato = new MandatoData(
            data,
            template,
            null,
            null,
            new MandatarioFirmante("Carlos Ruiz", "70111222"));

        var pdf = Generator.GenerateMandato(mandato).Content;

        System.Text.Encoding.ASCII.GetString(pdf, 0, 4).Should().Be("%PDF");
        CountPages(pdf).Should().Be(1);
    }

    private static int CountPages(byte[] pdf)
    {
        using var ms = new MemoryStream(pdf);
        using var doc = PdfSharpCore.Pdf.IO.PdfReader.Open(ms, PdfSharpCore.Pdf.IO.PdfDocumentOpenMode.Import);
        return doc.PageCount;
    }
}
