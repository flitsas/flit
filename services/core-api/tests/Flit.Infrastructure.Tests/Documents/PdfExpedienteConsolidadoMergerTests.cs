using System.Text;
using Flit.Infrastructure.Documents;
using Flit.Tramites.Application.Documents;
using FluentAssertions;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using Xunit;

namespace Flit.Infrastructure.Tests.Documents;

public sealed class PdfExpedienteConsolidadoMergerTests
{
    private static readonly PdfExpedienteConsolidadoMerger Merger = new();

    static PdfExpedienteConsolidadoMergerTests()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    [Fact]
    public void Merge_TwoSimplePdfs_ProducesPdfHeader()
    {
        var pdf1 = MinimalPdf();
        var pdf2 = MinimalPdf();

        var merged = Merger.Merge([pdf1, pdf2]);

        merged.Should().NotBeNullOrEmpty();
        Encoding.UTF8.GetString(merged.AsSpan(0, 4)).Should().Be("%PDF");
    }

    [Fact]
    public void NormalizeToPdf_PdfInput_ReturnsSameBytes()
    {
        var pdf = MinimalPdf();
        var normalized = Merger.NormalizeToPdf(pdf, "application/pdf");
        normalized.Should().BeEquivalentTo(pdf);
    }

    [Fact]
    public void NormalizeToPdf_PngInput_ProducesPdf()
    {
        var png = MinimalPng();
        var normalized = Merger.NormalizeToPdf(png, "image/png");

        Encoding.UTF8.GetString(normalized.AsSpan(0, 4)).Should().Be("%PDF");
    }

    private static byte[] MinimalPdf() =>
        Document.Create(c => c.Page(p => p.Content().Text("x"))).GeneratePdf();

    private static byte[] MinimalPng()
    {
        // 1×1 PNG transparente
        return Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");
    }
}
