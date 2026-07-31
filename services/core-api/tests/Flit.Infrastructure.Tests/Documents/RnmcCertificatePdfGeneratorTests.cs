using System.Text;
using Flit.Infrastructure.Documents;
using Flit.Tramites.Application.Documents;
using FluentAssertions;
using Xunit;

namespace Flit.Infrastructure.Tests.Documents;

/// <summary>Tests del certificado RNMC en PDF real (HU #10762).</summary>
public sealed class RnmcCertificatePdfGeneratorTests
{
    private static RnmcCertificateData FullData() => new(
        ProcedureInstanceId: Guid.NewGuid(),
        ReferenceNumber: "TRM-2026-000123",
        ConsultadoEn: new DateTimeOffset(2026, 7, 15, 10, 30, 0, TimeSpan.Zero),
        Entradas:
        [
            new RnmcCertificateEntry("comprador", "DANIEL AMADO GARCIA", "1193552679", "SIN MEDIDAS CORRECTIVAS", null),
            new RnmcCertificateEntry("vendedor", "MARIA LOPEZ", "52123456", "CON MEDIDAS CORRECTIVAS", "1 medida(s): Riña"),
        ]);

    [Fact]
    public void Generate_ProducesPdf_WithCertificadoRnmcTipoAndPdfFilename()
    {
        // AC1: certificado como application/pdf, tipo certificado_rnmc, filename .pdf con la referencia.
        var doc = new RnmcCertificatePdfGenerator().GenerateRnmcCertificate(FullData());

        Encoding.ASCII.GetString(doc.Content, 0, 4).Should().Be("%PDF");
        doc.Mimetype.Should().Be("application/pdf");
        doc.Tipo.Should().Be("certificado_rnmc");
        doc.Filename.Should().Be("certificado_rnmc_TRM-2026-000123.pdf");
    }

    [Fact]
    public void Generate_ReferenciaConSlash_SaneaElFilename()
    {
        // El filename no puede llevar '/': rompería la ruta de storage.
        var data = FullData() with { ReferenceNumber = "TRM/2026/000123" };

        var doc = new RnmcCertificatePdfGenerator().GenerateRnmcCertificate(data);

        doc.Filename.Should().Be("certificado_rnmc_TRM-2026-000123.pdf").And.NotContain("/");
    }

    [Fact]
    public void Generate_SinEntradas_ProducesPdf_WithoutThrowing()
    {
        // AC3: sin consulta RNMC el certificado se pinta igual (párrafo explícito, sin tabla vacía).
        var data = FullData() with { Entradas = [] };

        GeneratedDocument? doc = null;
        var act = () => doc = new RnmcCertificatePdfGenerator().GenerateRnmcCertificate(data);

        act.Should().NotThrow();
        doc!.Mimetype.Should().Be("application/pdf");
        Encoding.ASCII.GetString(doc.Content, 0, 4).Should().Be("%PDF");
    }

    [Fact]
    public void Generate_ConReferenciaVacia_ProducesPdf_WithoutThrowing()
    {
        // AC3: referencia vacía → marcador seguro ('-'), sin excepción.
        var data = FullData() with { ReferenceNumber = "  " };

        GeneratedDocument? doc = null;
        var act = () => doc = new RnmcCertificatePdfGenerator().GenerateRnmcCertificate(data);

        act.Should().NotThrow();
        doc!.Filename.Should().Be("certificado_rnmc_-.pdf");
        Encoding.ASCII.GetString(doc.Content, 0, 4).Should().Be("%PDF");
    }

    [Fact]
    public void Generate_ConCamposVacios_ProducesPdf_WithoutThrowing()
    {
        // AC3: entradas con nombre/documento/estado vacíos y detalle null → marcadores seguros.
        var data = FullData() with
        {
            Entradas = [new RnmcCertificateEntry("", "", "  ", "", null)],
        };

        GeneratedDocument? doc = null;
        var act = () => doc = new RnmcCertificatePdfGenerator().GenerateRnmcCertificate(data);

        act.Should().NotThrow();
        doc!.Mimetype.Should().Be("application/pdf");
        Encoding.ASCII.GetString(doc.Content, 0, 4).Should().Be("%PDF");
    }

    [Fact]
    public void Generate_MimetypeIsConsolidadoMergeable()
    {
        // AC2: application/pdf es aceptado por IsMergeableMime → se fusiona en el Expediente Consolidado.
        var doc = new RnmcCertificatePdfGenerator().GenerateRnmcCertificate(FullData());

        doc.Mimetype.Should().Be("application/pdf");
    }
}
