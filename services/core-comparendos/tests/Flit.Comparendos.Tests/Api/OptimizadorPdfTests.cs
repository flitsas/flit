using Flit.Comparendos.Api.Infraestructura.Archivos;
using FluentAssertions;
using Xunit;

namespace Flit.Comparendos.Tests.Api;

/// <summary>HU #14366 / GDC ID11 — política de PDF antes de MinIO.</summary>
public sealed class OptimizadorPdfTests
{
    [Fact]
    public void Preparar_NoTocaEvidenciaOcr()
    {
        var original = "%PDF-1.4 evidencia OCR"u8.ToArray();
        var r = OptimizadorPdf.Preparar("ocr", OptimizadorPdf.ContentTypePdf, original);
        r.Should().BeSameAs(original);
    }

    [Fact]
    public void Preparar_NoTocaImagenes()
    {
        var original = new byte[] { 0xFF, 0xD8, 0xFF };
        OptimizadorPdf.Preparar("dp", "image/jpeg", original).Should().BeSameAs(original);
    }

    [Fact]
    public void ExcluirDominio_Ocr()
    {
        OptimizadorPdf.ExcluirDominio("ocr").Should().BeTrue();
        OptimizadorPdf.ExcluirDominio("dp").Should().BeFalse();
    }
}
