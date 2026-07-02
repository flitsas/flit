using System.Text;
using Flit.Infrastructure.Ocr;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using PdfSharpCore.Pdf.IO;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;
using Xunit;

namespace Flit.Infrastructure.Tests.Ocr;

public sealed class PdfSharpPageExtractorTests
{
    private static readonly PdfSharpPageExtractor Extractor = new(NullLogger<PdfSharpPageExtractor>.Instance);

    static PdfSharpPageExtractorTests() => QuestPDF.Settings.License = LicenseType.Community;

    [Fact]
    public void Extrae_subconjunto_de_paginas_de_pdf_multipagina()
    {
        var pdf = MultiPagePdf(3);

        var result = Extractor.ExtractPages(pdf, [1, 3]);

        result.Should().NotBeNullOrEmpty();
        Encoding.UTF8.GetString(result!.AsSpan(0, 4)).Should().Be("%PDF");
        PageCount(result!).Should().Be(2);
    }

    [Fact]
    public void Ignora_paginas_fuera_de_rango_y_deduplica()
    {
        var pdf = MultiPagePdf(2);

        var result = Extractor.ExtractPages(pdf, [1, 1, 99]); // duplicada + fuera de rango → sólo la página 1

        PageCount(result!).Should().Be(1);
    }

    [Fact]
    public void Sin_paginas_validas_devuelve_null()
    {
        var pdf = MultiPagePdf(2);

        Extractor.ExtractPages(pdf, [0, 99]).Should().BeNull();
    }

    [Fact]
    public void Pdf_ilegible_devuelve_null()
    {
        byte[] garbage = [0x25, 0x50, 0x44, 0x46, 0x00, 0x01, 0x02];

        Extractor.ExtractPages(garbage, [1]).Should().BeNull();
    }

    private static byte[] MultiPagePdf(int pages) =>
        Document.Create(c =>
        {
            for (var i = 0; i < pages; i++)
                c.Page(p => p.Content().Text($"page {i + 1}"));
        }).GeneratePdf();

    private static int PageCount(byte[] pdf)
    {
        using var ms = new MemoryStream(pdf);
        using var doc = PdfReader.Open(ms, PdfDocumentOpenMode.InformationOnly);
        return doc.PageCount;
    }
}
