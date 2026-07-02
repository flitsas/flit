using System.Text.Json.Nodes;
using Flit.Tramites.Application.Ocr;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Tramites.Application.Tests.Ocr;

public sealed class AnalyzeDocumentHandlerMultipageTests
{
    private static readonly byte[] PdfBytes = [0x25, 0x50, 0x44, 0x46, 0x2D, 0x31, 0x2E, 0x37]; // %PDF-1.7
    private static readonly byte[] JpgBytes = [0xFF, 0xD8, 0xFF, 0xE0];
    private static readonly byte[] RecortadoBytes = [0x25, 0x50, 0x44, 0x46, 0xAA]; // PDF recortado simulado

    private readonly IDocumentOcrAnalyzer _analyzer = Substitute.For<IDocumentOcrAnalyzer>();

    private void StubAnalyzer(JsonObject data) =>
        _analyzer.AnalyzeAsync(Arg.Any<string>(), Arg.Any<ReadOnlyMemory<byte>>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new DocumentOcrAnalysis(true, data));

    [Fact]
    public async Task Pdf_con_subconjunto_de_paginas_devuelve_recorte_base64_y_anota_data()
    {
        var data = new JsonObject
        {
            ["es_factura_valida"] = true,
            ["total_paginas"] = 3,
            ["paginas_documento"] = new JsonArray(1),
        };
        StubAnalyzer(data);
        var extractor = new FakeExtractor { Result = RecortadoBytes };
        var handler = new AnalyzeDocumentHandler(_analyzer, extractor);

        var (result, failure) = await handler.HandleAsync("factura", PdfBytes, TestContext.Current.CancellationToken);

        failure.Should().BeNull();
        result!.ExtractedPdfBase64.Should().Be(Convert.ToBase64String(RecortadoBytes));
        extractor.Calls.Should().Be(1);
        extractor.ReceivedPages.Should().Equal(1); // base 1, tal cual lo reporta el modelo
        result.Data!["_paginas_extraidas"]!.GetValue<bool>().Should().BeTrue();
        result.Data["_paginas_originales"]!.GetValue<int>().Should().Be(3);
    }

    [Fact]
    public async Task Pdf_de_una_sola_pagina_o_todas_no_recorta()
    {
        var data = new JsonObject
        {
            ["es_valido"] = true,
            ["total_paginas"] = 2,
            ["paginas_documento"] = new JsonArray(1, 2),
        };
        StubAnalyzer(data);
        var extractor = new FakeExtractor();
        var handler = new AnalyzeDocumentHandler(_analyzer, extractor);

        var (result, failure) = await handler.HandleAsync("impronta", PdfBytes, TestContext.Current.CancellationToken);

        failure.Should().BeNull();
        result!.ExtractedPdfBase64.Should().BeNull();
        extractor.Calls.Should().Be(0);
        result.Data!.ContainsKey("_paginas_extraidas").Should().BeFalse();
    }

    [Fact]
    public async Task Imagen_no_pasa_por_recorte_aunque_haya_subconjunto()
    {
        var data = new JsonObject
        {
            ["es_valido"] = true,
            ["total_paginas"] = 3,
            ["paginas_documento"] = new JsonArray(2),
        };
        StubAnalyzer(data);
        var extractor = new FakeExtractor { Result = RecortadoBytes };
        var handler = new AnalyzeDocumentHandler(_analyzer, extractor);

        var (result, _) = await handler.HandleAsync("soat", JpgBytes, TestContext.Current.CancellationToken);

        result!.ExtractedPdfBase64.Should().BeNull();
        extractor.Calls.Should().Be(0);
    }

    [Fact]
    public async Task Sin_extractor_registrado_no_recorta()
    {
        var data = new JsonObject
        {
            ["es_valido"] = true,
            ["total_paginas"] = 5,
            ["paginas_documento"] = new JsonArray(2, 3),
        };
        StubAnalyzer(data);
        var handler = new AnalyzeDocumentHandler(_analyzer); // pdfExtractor = null

        var (result, _) = await handler.HandleAsync("aduana", PdfBytes, TestContext.Current.CancellationToken);

        result!.ExtractedPdfBase64.Should().BeNull();
    }

    [Fact]
    public async Task Extractor_devuelve_null_no_recorta_y_no_anota()
    {
        var data = new JsonObject
        {
            ["es_valido"] = true,
            ["total_paginas"] = 4,
            ["paginas_documento"] = new JsonArray(1),
        };
        StubAnalyzer(data);
        var extractor = new FakeExtractor { Result = null }; // PDF ilegible / recorte fallido
        var handler = new AnalyzeDocumentHandler(_analyzer, extractor);

        var (result, _) = await handler.HandleAsync("factura", PdfBytes, TestContext.Current.CancellationToken);

        result!.ExtractedPdfBase64.Should().BeNull();
        extractor.Calls.Should().Be(1);
        result.Data!.ContainsKey("_paginas_extraidas").Should().BeFalse();
    }

    private sealed class FakeExtractor : IPdfPageExtractor
    {
        public byte[]? Result { get; set; } = [0x25, 0x50, 0x44, 0x46];
        public IReadOnlyList<int>? ReceivedPages { get; private set; }
        public int Calls { get; private set; }

        public byte[]? ExtractPages(ReadOnlyMemory<byte> pdf, IReadOnlyList<int> pages)
        {
            Calls++;
            ReceivedPages = pages;
            return Result;
        }
    }
}
