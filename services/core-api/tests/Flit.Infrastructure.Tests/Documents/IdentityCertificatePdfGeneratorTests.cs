using System.Text;
using Flit.Infrastructure.Documents;
using Flit.Tramites.Application.Documents;
using FluentAssertions;
using Xunit;

namespace Flit.Infrastructure.Tests.Documents;

/// <summary>Tests del certificado de validación de identidad en PDF real (HU #10458).</summary>
public sealed class IdentityCertificatePdfGeneratorTests
{
    private static IdentityCertificateData FullData() => new(
        ProcedureInstanceId: Guid.NewGuid(),
        ReferenceNumber: "TRM-2026-000123",
        CompradorNombre: "DANIEL AMADO GARCIA",
        CompradorDocumento: "1193552679",
        Score: 98,
        Resultado: "APROBADO");

    [Fact]
    public void Generate_ProducesPdf_WithCertificadoTipoAndPdfFilename()
    {
        // AC1: certificado como application/pdf, tipo certificado_identidad, filename .pdf.
        var doc = new IdentityCertificatePdfGenerator().GenerateIdentityCertificate(FullData());

        Encoding.ASCII.GetString(doc.Content, 0, 4).Should().Be("%PDF");
        doc.Mimetype.Should().Be("application/pdf");
        doc.Tipo.Should().Be("certificado_identidad");
        doc.Filename.Should().EndWith(".pdf").And.Contain("TRM-2026-000123");
    }

    [Fact]
    public void Generate_WithIncompleteIdentity_ProducesPdf_WithoutThrowing()
    {
        // AC2: sin nombre/documento → PDF con marcadores seguros, sin excepción ni text/plain.
        var data = FullData() with { CompradorNombre = "", CompradorDocumento = "  " };

        GeneratedDocument? doc = null;
        var act = () => doc = new IdentityCertificatePdfGenerator().GenerateIdentityCertificate(data);

        act.Should().NotThrow();
        doc!.Mimetype.Should().Be("application/pdf");
        Encoding.ASCII.GetString(doc.Content, 0, 4).Should().Be("%PDF");
    }

    [Fact]
    public void Generate_MimetypeIsConsolidadoMergeable()
    {
        // AC3: application/pdf es aceptado por IsMergeableMime (pdf/jpeg/png/webp); hoy el
        // certificado .txt (text/plain) se descarta del consolidado.
        var doc = new IdentityCertificatePdfGenerator().GenerateIdentityCertificate(FullData());

        doc.Mimetype.Should().Be("application/pdf");
    }
}
