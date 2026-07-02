using System.Text.Json.Nodes;
using Flit.Tramites.Application.Ocr;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Tramites.Application.Tests.Ocr;

public sealed class AnalyzeDocumentHandlerTests
{
    private static readonly byte[] PdfBytes = [0x25, 0x50, 0x44, 0x46, 0x2D, 0x31, 0x2E, 0x37]; // %PDF-1.7
    private static readonly byte[] JpgBytes = [0xFF, 0xD8, 0xFF, 0xE0];
    private static readonly byte[] PngBytes = [0x89, 0x50, 0x4E, 0x47];
    private static readonly byte[] GarbageBytes = [0x00, 0x01, 0x02, 0x03];

    private readonly IDocumentOcrAnalyzer _analyzer = Substitute.For<IDocumentOcrAnalyzer>();
    private readonly AnalyzeDocumentHandler _handler;

    public AnalyzeDocumentHandlerTests() => _handler = new AnalyzeDocumentHandler(_analyzer);

    [Fact]
    public async Task Tipo_no_soportado_devuelve_400_y_no_llama_al_analyzer()
    {
        var (result, failure) = await _handler.HandleAsync("cotizacion", PdfBytes, TestContext.Current.CancellationToken);

        result.Should().BeNull();
        failure.Should().NotBeNull();
        failure!.Status.Should().Be(400);
        await _analyzer.DidNotReceiveWithAnyArgs()
            .AnalyzeAsync(Arg.Any<string>(), Arg.Any<ReadOnlyMemory<byte>>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Archivo_vacio_devuelve_400()
    {
        var (result, failure) = await _handler.HandleAsync("factura", Array.Empty<byte>(), TestContext.Current.CancellationToken);

        result.Should().BeNull();
        failure!.Status.Should().Be(400);
        failure.Message.Should().Contain("requerido");
    }

    [Fact]
    public async Task Archivo_mayor_a_10MB_devuelve_400()
    {
        var big = new byte[AnalyzeDocumentHandler.MaxFileBytes + 1];
        big[0] = 0x25; big[1] = 0x50; big[2] = 0x44; big[3] = 0x46; // %PDF, para aislar la regla de tamaño

        var (result, failure) = await _handler.HandleAsync("factura", big, TestContext.Current.CancellationToken);

        result.Should().BeNull();
        failure!.Status.Should().Be(400);
        failure.Message.Should().Contain("10MB");
    }

    [Fact]
    public async Task Formato_no_pdf_jpg_png_devuelve_400()
    {
        var (result, failure) = await _handler.HandleAsync("factura", GarbageBytes, TestContext.Current.CancellationToken);

        result.Should().BeNull();
        failure!.Status.Should().Be(400);
        failure.Message.Should().Contain("PDF");
    }

    [Theory]
    [InlineData(nameof(PdfBytes), "application/pdf")]
    [InlineData(nameof(JpgBytes), "image/jpeg")]
    [InlineData(nameof(PngBytes), "image/png")]
    public async Task Resuelve_media_type_por_magic_bytes_y_delega(string sample, string expectedMediaType)
    {
        var bytes = sample switch
        {
            nameof(PdfBytes) => PdfBytes,
            nameof(JpgBytes) => JpgBytes,
            _ => PngBytes,
        };
        string? capturedMediaType = null;
        _analyzer.AnalyzeAsync(Arg.Any<string>(), Arg.Any<ReadOnlyMemory<byte>>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ReturnsForAnyArgs(ci =>
            {
                capturedMediaType = ci.ArgAt<string>(2);
                return new DocumentOcrAnalysis(true, new JsonObject { ["es_valido"] = true });
            });

        var (result, failure) = await _handler.HandleAsync("impronta", bytes, TestContext.Current.CancellationToken);

        failure.Should().BeNull();
        result.Should().NotBeNull();
        result!.Ok.Should().BeTrue();
        result.Tipo.Should().Be("impronta");
        capturedMediaType.Should().Be(expectedMediaType);
    }

    [Fact]
    public async Task Analyzer_ok_devuelve_data()
    {
        _analyzer.AnalyzeAsync(Arg.Any<string>(), Arg.Any<ReadOnlyMemory<byte>>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ReturnsForAnyArgs(new DocumentOcrAnalysis(true, new JsonObject { ["es_factura_valida"] = true }));

        var (result, failure) = await _handler.HandleAsync("factura", PdfBytes, TestContext.Current.CancellationToken);

        failure.Should().BeNull();
        result!.Data.Should().NotBeNull();
        result.Data!["es_factura_valida"]!.GetValue<bool>().Should().BeTrue();
    }

    [Fact]
    public async Task Analyzer_degradado_propaga_status_y_mensaje()
    {
        _analyzer.AnalyzeAsync(Arg.Any<string>(), Arg.Any<ReadOnlyMemory<byte>>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ReturnsForAnyArgs(new DocumentOcrAnalysis(false, null, 503, "Servicio no disponible"));

        var (result, failure) = await _handler.HandleAsync("soat", PdfBytes, TestContext.Current.CancellationToken);

        result.Should().BeNull();
        failure.Should().NotBeNull();
        failure!.Status.Should().Be(503);
        failure.Message.Should().Be("Servicio no disponible");
    }
}
