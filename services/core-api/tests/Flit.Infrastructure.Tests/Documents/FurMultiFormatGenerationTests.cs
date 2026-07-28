using System.Text;
using Flit.Infrastructure.Documents.Fur;
using Flit.Tramites.Application.Documents;
using FluentAssertions;
using PdfSharpCore.Pdf.IO;
using Xunit;

namespace Flit.Infrastructure.Tests.Documents;

/// <summary>
/// HU #10925 (Feature #10918) — integración de la generación multi-plantilla: cada FurTemplateFormat
/// produce un PDF válido de 2 páginas usando su propia plantilla. Verifica la selección end-to-end del
/// generador (registro de assets por formato + fallback a AUTOMOTOR para formatos no incorporados).
/// </summary>
public sealed class FurMultiFormatGenerationTests
{
    private static FurDocumentData Sample(FurTemplateFormat format) => new(
        ProcedureInstanceId: Guid.NewGuid(),
        ReferenceNumber: "TRM-2026-000010",
        Modalidad: "traspaso",
        TipologiaCodigo: "traspaso_standard",
        Vehiculo: new VehiculoDatos(
            Marca: "MARCA", Linea: "LINEA", Modelo: "2024", Color: "ROJO",
            Clase: "EXCAVADORA", Combustible: "DIESEL", Cilindraje: "0",
            Vin: "VIN123", Placa: "ABC123", NumeroMotor: "M1", NumeroSerie: "S1"),
        Organismo: new OrganismoTransito("05001", "OT", "CIUDAD"),
        Partes:
        [
            new DocumentParte("vendedor", "APELLIDO1 APELLIDO2 NOMBRE", "111", null, DocumentType: "CC"),
            new DocumentParte("comprador", "OTRO1 OTRO2 NOMBRE", "222", null, DocumentType: "CC"),
        ],
        ValorVenta: 1000m, Causal: null, SellosFirma: [],
        FechaTramite: new DateTime(2026, 7, 24),
        TemplateFormat: format);

    [Theory]
    [InlineData(FurTemplateFormat.Automotor)]
    [InlineData(FurTemplateFormat.Maquinaria)]
    [InlineData(FurTemplateFormat.Remolques)]
    public void GenerateFur_ProducesTwoPagePdf_PerFormat(FurTemplateFormat format)
    {
        var doc = new FurOverlayDocumentGenerator().GenerateFur(Sample(format));

        doc.Tipo.Should().Be("fur");
        doc.Mimetype.Should().Be("application/pdf");
        Encoding.ASCII.GetString(doc.Content, 0, 4).Should().Be("%PDF");

        using var ms = new MemoryStream(doc.Content);
        var pdf = PdfReader.Open(ms, PdfDocumentOpenMode.InformationOnly);
        pdf.PageCount.Should().Be(2, "formulario (overlay) + instrucciones");
    }
}
