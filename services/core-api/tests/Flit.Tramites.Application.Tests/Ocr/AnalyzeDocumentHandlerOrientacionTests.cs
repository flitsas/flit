using System.Text.Json.Nodes;
using Flit.Tramites.Application.Ocr;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Flit.Tramites.Application.Tests.Ocr;

/// <summary>
/// HU #12036 — el enderezado visto desde el handler. Aquí se fija la frontera que más importa: se
/// endereza SOLO lo que se manda al modelo; lo que acaba en el expediente sigue saliendo del binario
/// que subió el usuario.
/// </summary>
public sealed class AnalyzeDocumentHandlerOrientacionTests
{
    private static readonly byte[] PdfOriginal = [0x25, 0x50, 0x44, 0x46, 0x2D, 0x31, 0x2E, 0x37];
    private static readonly byte[] JpgBytes = [0xFF, 0xD8, 0xFF, 0xE0];
    private static readonly byte[] PdfEnderezado = [0x25, 0x50, 0x44, 0x46, 0xBB];

    private readonly IDocumentOcrAnalyzer _analyzer = Substitute.For<IDocumentOcrAnalyzer>();

    private void StubAnalyzer() =>
        _analyzer.AnalyzeAsync(Arg.Any<string>(), Arg.Any<ReadOnlyMemory<byte>>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new DocumentOcrAnalysis(true, new JsonObject { ["es_valido"] = true }));

    private static PdfOrientationNormalizer Normalizador(IPdfPageExtractor pages, PageOrientation primera) =>
        new(pages, new FakeProbe(primera), NullLogger<PdfOrientationNormalizer>.Instance);

    [Fact]
    public async Task El_modelo_recibe_el_PDF_enderezado()
    {
        StubAnalyzer();
        var pages = new FakePages();
        var handler = new AnalyzeDocumentHandler(_analyzer, pages, Normalizador(pages, PageOrientation.Rotated));

        await handler.HandleAsync("factura", PdfOriginal, TestContext.Current.CancellationToken);

        await _analyzer.Received(1).AnalyzeAsync(
            "factura",
            Arg.Is<ReadOnlyMemory<byte>>(b => b.ToArray().SequenceEqual(PdfEnderezado)),
            "application/pdf",
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task El_recorte_para_el_expediente_sale_del_ORIGINAL_no_del_enderezado()
    {
        // El enderezado es una ayuda para leer, no una edición del documento del usuario. Si se colara
        // al expediente, un giro equivocado quedaría archivado para siempre.
        _analyzer.AnalyzeAsync(Arg.Any<string>(), Arg.Any<ReadOnlyMemory<byte>>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new DocumentOcrAnalysis(true, new JsonObject
            {
                ["total_paginas"] = 3,
                ["paginas_documento"] = new JsonArray(2),
            }));
        var pages = new FakePages();
        var handler = new AnalyzeDocumentHandler(_analyzer, pages, Normalizador(pages, PageOrientation.Rotated));

        await handler.HandleAsync("factura", PdfOriginal, TestContext.Current.CancellationToken);

        pages.RecorteRecibio.Should().Equal(PdfOriginal);
    }

    [Fact]
    public async Task Una_imagen_no_pasa_por_el_enderezado()
    {
        // Solo hay /Rotate que reescribir en un PDF; con un JPG no aplica y no debe gastarse la sonda.
        StubAnalyzer();
        var pages = new FakePages();
        var probe = new FakeProbe(PageOrientation.Rotated);
        var handler = new AnalyzeDocumentHandler(
            _analyzer, pages, new PdfOrientationNormalizer(pages, probe, NullLogger<PdfOrientationNormalizer>.Instance));

        await handler.HandleAsync("factura", JpgBytes, TestContext.Current.CancellationToken);

        probe.Calls.Should().Be(0);
    }

    [Fact]
    public async Task Sin_normalizador_registrado_el_flujo_sigue_igual_que_antes()
    {
        // Con el proveedor mock no hay a quién preguntarle: el handler lo trata como opcional.
        StubAnalyzer();
        var handler = new AnalyzeDocumentHandler(_analyzer, new FakePages());

        var (result, failure) = await handler.HandleAsync("factura", PdfOriginal, TestContext.Current.CancellationToken);

        failure.Should().BeNull();
        await _analyzer.Received(1).AnalyzeAsync(
            "factura",
            Arg.Is<ReadOnlyMemory<byte>>(b => b.ToArray().SequenceEqual(PdfOriginal)),
            "application/pdf",
            Arg.Any<CancellationToken>());
    }

    private sealed class FakePages : IPdfPageExtractor
    {
        /// <summary>Bytes con los que se pidió el RECORTE (no la página de sonda).</summary>
        public byte[]? RecorteRecibio { get; private set; }

        public int? CountPages(ReadOnlyMemory<byte> pdf) => 3;

        public byte[]? ExtractPages(ReadOnlyMemory<byte> pdf, IReadOnlyList<int> pages)
        {
            // La sonda pide siempre la página 1; el recorte pide las que dijo el modelo.
            if (pages.Count == 1 && pages[0] == 1 && RecorteRecibio is null && pdf.Length == PdfOriginal.Length)
                return [0x25, 0x50, 0x44, 0x46, 0xAA];

            RecorteRecibio = pdf.ToArray();
            return [0x25, 0x50, 0x44, 0x46, 0xCC];
        }

        public byte[]? Rotate(ReadOnlyMemory<byte> pdf, int quarterTurns) => PdfEnderezado;
    }

    private sealed class FakeProbe(PageOrientation primera) : IDocumentOrientationProbe
    {
        public int Calls { get; private set; }

        public Task<PageOrientation> ProbeAsync(ReadOnlyMemory<byte> pdf, CancellationToken ct)
        {
            Calls++;
            // La primera respuesta la fija el test; a partir de ahí se ve derecha, para que el
            // normalizador termine en una vuelta.
            return Task.FromResult(Calls == 1 ? primera : PageOrientation.Upright);
        }
    }
}
