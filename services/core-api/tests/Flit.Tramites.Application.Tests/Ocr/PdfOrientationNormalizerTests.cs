using Flit.Tramites.Application.Ocr;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Flit.Tramites.Application.Tests.Ocr;

/// <summary>
/// HU #12036 — el enderezado previo al análisis.
///
/// El defecto que cierra: un escaneo girado no hace que el modelo lea mal, hace que INVENTE, y lo hace
/// con `es_valido: true` y sin ninguna señal. Estas pruebas fijan las tres decisiones del diseño:
/// se prueba el giro en vez de deducirlo, se sondea una sola página, y ante cualquier duda se devuelve
/// el original sin tocar.
/// </summary>
public sealed class PdfOrientationNormalizerTests
{
    private static readonly ReadOnlyMemory<byte> Pdf = new byte[] { 0x25, 0x50, 0x44, 0x46, 0x01 };

    private static PdfOrientationNormalizer Sut(IPdfPageExtractor pages, IDocumentOrientationProbe probe) =>
        new(pages, probe, NullLogger<PdfOrientationNormalizer>.Instance);

    [Fact]
    public async Task Si_la_pagina_ya_esta_derecha_no_toca_el_documento()
    {
        var pages = new FakePages();
        var probe = new FakeProbe(PageOrientation.Upright);

        var salida = await Sut(pages, probe).NormalizeAsync(Pdf, TestContext.Current.CancellationToken);

        salida.ToArray().Should().Equal(Pdf.ToArray());
        pages.RotateCalls.Should().Be(0, "no hay nada que enderezar");
        probe.Calls.Should().Be(1, "una sola sonda basta cuando la respuesta es que sí");
    }

    [Fact]
    public async Task Solo_sondea_la_primera_pagina_para_que_la_llamada_sea_barata()
    {
        // Un expediente de 25 páginas costaría ~26.500 tokens por sonda; una sola página, ~3.300.
        var pages = new FakePages();
        await Sut(pages, new FakeProbe(PageOrientation.Upright)).NormalizeAsync(Pdf, TestContext.Current.CancellationToken);

        pages.ExtractedPages.Should().Equal(1);
    }

    [Fact]
    public async Task Gira_el_documento_entero_los_mismos_cuartos_que_enderezaron_la_sonda()
    {
        // La sonda se ve derecha al segundo cuarto de vuelta ⇒ el documento se gira 180°, no 90°.
        var pages = new FakePages();
        var probe = new FakeProbe(PageOrientation.Rotated, PageOrientation.Rotated, PageOrientation.Upright);

        var salida = await Sut(pages, probe).NormalizeAsync(Pdf, TestContext.Current.CancellationToken);

        pages.RotacionAplicadaAlDocumento.Should().Be(2);
        salida.ToArray().Should().Equal(pages.UltimoRotado!);
    }

    [Fact]
    public async Task Prueba_los_giros_en_vez_de_preguntar_la_direccion()
    {
        // Preguntar hacia qué lado estaba girada acertó 3 de 4 veces en la medición; «¿está derecha?»
        // sale fiable. Por eso se itera: sonda, giro, sonda otra vez.
        var pages = new FakePages();
        var probe = new FakeProbe(PageOrientation.Rotated, PageOrientation.Rotated, PageOrientation.Rotated, PageOrientation.Upright);

        await Sut(pages, probe).NormalizeAsync(Pdf, TestContext.Current.CancellationToken);

        probe.Calls.Should().Be(4);
        pages.RotacionAplicadaAlDocumento.Should().Be(3);
    }

    [Fact]
    public async Task Si_ninguna_vuelta_la_endereza_devuelve_el_original()
    {
        var pages = new FakePages();
        var probe = new FakeProbe(PageOrientation.Rotated, PageOrientation.Rotated, PageOrientation.Rotated, PageOrientation.Rotated);

        var salida = await Sut(pages, probe).NormalizeAsync(Pdf, TestContext.Current.CancellationToken);

        salida.ToArray().Should().Equal(Pdf.ToArray());
        pages.RotacionAplicadaAlDocumento.Should().BeNull("no se llegó a girar el documento");
    }

    [Fact]
    public async Task Ante_una_sonda_que_no_sabe_deja_el_documento_como_esta()
    {
        // Unknown = proveedor caído o respuesta ilegible. Girar a ciegas sería peor que no hacer nada.
        var pages = new FakePages();

        var salida = await Sut(pages, new FakeProbe(PageOrientation.Unknown)).NormalizeAsync(Pdf, TestContext.Current.CancellationToken);

        salida.ToArray().Should().Equal(Pdf.ToArray());
        pages.RotateCalls.Should().Be(0);
    }

    [Fact]
    public async Task Si_el_PDF_es_ilegible_no_se_sondea_siquiera()
    {
        var pages = new FakePages { ExtractDevuelveNull = true };
        var probe = new FakeProbe(PageOrientation.Rotated);

        var salida = await Sut(pages, probe).NormalizeAsync(Pdf, TestContext.Current.CancellationToken);

        salida.ToArray().Should().Equal(Pdf.ToArray());
        probe.Calls.Should().Be(0, "sin página que mirar no hay nada que preguntar");
    }

    [Fact]
    public async Task Si_el_giro_falla_a_media_iteracion_devuelve_el_original()
    {
        var pages = new FakePages { RotateDevuelveNull = true };
        var probe = new FakeProbe(PageOrientation.Rotated, PageOrientation.Upright);

        var salida = await Sut(pages, probe).NormalizeAsync(Pdf, TestContext.Current.CancellationToken);

        salida.ToArray().Should().Equal(Pdf.ToArray());
    }

    private sealed class FakePages : IPdfPageExtractor
    {
        private int _rotacionAcumuladaEnLaSonda;

        public bool ExtractDevuelveNull { get; init; }
        public bool RotateDevuelveNull { get; init; }
        public List<int> ExtractedPages { get; } = [];
        public int RotateCalls { get; private set; }
        /// <summary>Cuartos de vuelta con los que se llamó a girar el DOCUMENTO (no la página de sonda).</summary>
        public int? RotacionAplicadaAlDocumento { get; private set; }
        public byte[]? UltimoRotado { get; private set; }

        public int? CountPages(ReadOnlyMemory<byte> pdf) => 1;

        public byte[]? ExtractPages(ReadOnlyMemory<byte> pdf, IReadOnlyList<int> pages)
        {
            ExtractedPages.AddRange(pages);
            return ExtractDevuelveNull ? null : [0x25, 0x50, 0x44, 0x46, 0xAA];
        }

        public byte[]? Rotate(ReadOnlyMemory<byte> pdf, int quarterTurns)
        {
            RotateCalls++;
            if (RotateDevuelveNull)
                return null;

            // El normalizador gira la sonda de a un cuarto y el documento de una vez: se distinguen
            // por el tamaño del binario, que es como el fake sabe cuál le están pasando.
            if (quarterTurns == 1 && pdf.Length == 5 && pdf.Span[4] == 0xAA)
            {
                _rotacionAcumuladaEnLaSonda++;
                return [0x25, 0x50, 0x44, 0x46, 0xAA];
            }

            RotacionAplicadaAlDocumento = quarterTurns;
            UltimoRotado = [0x25, 0x50, 0x44, 0x46, 0xBB];
            return UltimoRotado;
        }
    }

    private sealed class FakeProbe(params PageOrientation[] respuestas) : IDocumentOrientationProbe
    {
        public int Calls { get; private set; }

        public Task<PageOrientation> ProbeAsync(ReadOnlyMemory<byte> pdf, CancellationToken ct)
        {
            var r = Calls < respuestas.Length ? respuestas[Calls] : respuestas[^1];
            Calls++;
            return Task.FromResult(r);
        }
    }
}
