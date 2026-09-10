using System.IO.Compression;
using System.Text.Json.Nodes;
using Flit.Tramites.Application.Ocr;
using FluentAssertions;
using Xunit;

namespace Flit.Tramites.Application.Tests.Ocr;

/// <summary>
/// Handler del cargue masivo. Lo que se protege: que nada se suba solo, que un archivo malo no tumbe el
/// lote entero, que las páginas que sobran lleguen a la bandeja del operador, y los topes que evitan que
/// una carpeta grande se convierta en una factura de tokens.
/// </summary>
public sealed class AnalyzeBatchHandlerTests
{
    private static readonly string[] Matricula = ["factura", "aduana", "impronta", "soat", "rtm"];
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private static byte[] Pdf(int filler = 32) => [0x25, 0x50, 0x44, 0x46, .. new byte[filler]];
    private static byte[] Jpg() => [0xFF, 0xD8, 0x00, 0x00];

    // ── Camino feliz ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Reparte_cada_documento_reconocido_en_su_pieza_recortada()
    {
        var handler = Handler(
            Classifier(16,
                Doc("aduana", [2, 3, 4], 0.93),
                Doc("factura", [5, 6, 7], 0.97),
                Doc("impronta", [14], 0.88)),
            noReconocidas: [1, 8, 9]);

        var (r, failure) = await handler.HandleAsync(Matricula, [File("expediente.pdf", Pdf())], Ct);

        failure.Should().BeNull();
        r!.Piezas.Should().HaveCount(3);
        r.Piezas.Select(p => p.Tipo).Should().Equal("aduana", "factura", "impronta");
        r.Piezas[0].Paginas.Should().Equal(2, 3, 4);
        r.Piezas[0].TotalPaginasOrigen.Should().Be(16);
        r.Piezas[0].Filename.Should().Be("aduana_expediente.pdf");
        r.Piezas[0].ContentBase64.Should().NotBeNullOrWhiteSpace();
        r.Piezas[0].Data.Should().NotBeNull();
        r.NoReconocidos.Should().ContainSingle().Which.Paginas.Should().Equal(1, 8, 9);
        r.Errores.Should().BeEmpty();
    }

    [Fact]
    public async Task Las_piezas_salen_ordenadas_por_archivo_y_pagina()
    {
        var handler = Handler(
            Classifier(10, Doc("impronta", [7]), Doc("soat", [2]), Doc("factura", [4])));

        var (r, _) = await handler.HandleAsync(Matricula, [File("b.pdf", Pdf()), File("a.pdf", Pdf())], Ct);

        r!.Piezas.Select(p => (p.SourceFilename, p.Paginas[0]))
            .Should().Equal(("a.pdf", 2), ("a.pdf", 4), ("a.pdf", 7), ("b.pdf", 2), ("b.pdf", 4), ("b.pdf", 7));
    }

    [Fact]
    public async Task Un_documento_que_abarca_todo_el_pdf_no_se_recorta()
    {
        var handler = Handler(Classifier(1, Doc("soat", [1])));

        var (r, _) = await handler.HandleAsync(Matricula, [File("soat.pdf", Pdf())], Ct);

        // Sin recorte se conserva el archivo original tal cual, nombre incluido.
        r!.Piezas.Should().ContainSingle().Which.Filename.Should().Be("soat.pdf");
    }

    [Fact]
    public async Task Una_imagen_se_clasifica_entera_sin_intentar_partirla()
    {
        var handler = Handler(Classifier(1, Doc("soat", [1])));

        var (r, _) = await handler.HandleAsync(Matricula, [File("foto.jpg", Jpg())], Ct);

        var pieza = r!.Piezas.Should().ContainSingle().Subject;
        pieza.Mimetype.Should().Be("image/jpeg");
        pieza.Filename.Should().Be("foto.jpg");
    }

    [Fact]
    public async Task Si_el_recorte_falla_se_propone_el_archivo_original()
    {
        // Degradar a "de más" es preferible a perder el documento.
        var handler = Handler(Classifier(9, Doc("soat", [3])), extractor: new FakeExtractor(9, extractFails: true));

        var (r, _) = await handler.HandleAsync(Matricula, [File("expediente.pdf", Pdf())], Ct);

        r!.Piezas.Should().ContainSingle().Which.Filename.Should().Be("expediente.pdf");
    }

    // ── El OCR por tipo falla, pero la pieza sobrevive ────────────────────────

    [Fact]
    public async Task Si_el_analisis_de_una_pieza_falla_la_pieza_sigue_proponiendose_con_el_motivo()
    {
        var handler = Handler(
            Classifier(4, Doc("soat", [1])),
            analyzer: new FakeAnalyzer(fails: true));

        var (r, _) = await handler.HandleAsync(Matricula, [File("expediente.pdf", Pdf())], Ct);

        var pieza = r!.Piezas.Should().ContainSingle().Subject;
        pieza.Data.Should().BeNull();
        pieza.AnalisisError.Should().NotBeNullOrWhiteSpace();
        pieza.ContentBase64.Should().NotBeNullOrWhiteSpace();
    }

    // ── Nada aprovechable ────────────────────────────────────────────────────

    [Fact]
    public async Task Un_archivo_sin_nada_reconocible_va_entero_a_no_reconocidos()
    {
        var handler = Handler(Classifier(6), noReconocidas: [1, 2]);

        var (r, failure) = await handler.HandleAsync(Matricula, [File("mandato.pdf", Pdf())], Ct);

        failure.Should().BeNull();
        r!.Piezas.Should().BeEmpty();
        r.NoReconocidos.Should().ContainSingle().Which.Paginas.Should().Equal(1, 2);
    }

    [Fact]
    public async Task Sin_paginas_senaladas_se_reportan_todas_como_no_reconocidas()
    {
        var handler = Handler(Classifier(3));

        var (r, _) = await handler.HandleAsync(Matricula, [File("otro.pdf", Pdf())], Ct);

        r!.NoReconocidos.Should().ContainSingle().Which.Paginas.Should().Equal(1, 2, 3);
    }

    [Fact]
    public async Task El_clasificador_caido_es_un_error_del_archivo_no_del_lote()
    {
        var handler = Handler(new FakeClassifier(BatchClassification.Failure(503, "Servicio no disponible.")));

        var (r, failure) = await handler.HandleAsync(
            Matricula, [File("a.pdf", Pdf()), File("b.pdf", Pdf())], Ct);

        failure.Should().BeNull();
        r!.Errores.Should().HaveCount(2);
        r.Errores[0].Motivo.Should().Be("Servicio no disponible.");
    }

    // ── Validación por archivo ───────────────────────────────────────────────

    [Fact]
    public async Task Un_archivo_ilegible_no_tumba_los_demas()
    {
        var handler = Handler(Classifier(1, Doc("soat", [1])));

        var (r, _) = await handler.HandleAsync(
            Matricula, [File("raro.txt", [0x68, 0x6F, 0x6C, 0x61]), File("bueno.pdf", Pdf())], Ct);

        r!.Piezas.Should().ContainSingle().Which.SourceFilename.Should().Be("bueno.pdf");
        r.Errores.Should().ContainSingle().Which.Filename.Should().Be("raro.txt");
    }

    [Fact]
    public async Task Un_pdf_que_el_lector_local_no_abre_igual_se_clasifica()
    {
        // El modelo de visión lee PDFs que PdfSharp rechaza: no poder contar páginas cuesta el recorte,
        // no el documento. Se propone el archivo entero.
        var classifier = Classifier(4, Doc("soat", [2]));
        var handler = Handler(classifier, extractor: new FakeExtractor(null));

        var (r, _) = await handler.HandleAsync(Matricula, [File("raro.pdf", Pdf())], Ct);

        classifier.Llamadas.Should().Be(1);
        r!.Errores.Should().BeEmpty();
        var pieza = r.Piezas.Should().ContainSingle().Subject;
        pieza.Filename.Should().Be("raro.pdf");
        pieza.TotalPaginasOrigen.Should().Be(4); // el total lo aporta el clasificador
    }

    [Fact]
    public async Task Un_pdf_ilegible_para_ambos_se_reporta_con_el_motivo_del_clasificador()
    {
        var handler = Handler(
            new FakeClassifier(BatchClassification.Failure(503, "No se pudo analizar el archivo.")),
            extractor: new FakeExtractor(null));

        var (r, _) = await handler.HandleAsync(Matricula, [File("roto.pdf", Pdf())], Ct);

        r!.Errores.Should().ContainSingle().Which.Motivo.Should().Be("No se pudo analizar el archivo.");
    }

    [Fact]
    public async Task Un_pdf_con_demasiadas_paginas_se_rechaza_antes_de_gastar_el_modelo()
    {
        var classifier = Classifier(1, Doc("soat", [1]));
        var handler = Handler(classifier, extractor: new FakeExtractor(AnalyzeBatchHandler.MaxPdfPages + 1));

        var (r, _) = await handler.HandleAsync(Matricula, [File("enorme.pdf", Pdf())], Ct);

        r!.Errores.Should().ContainSingle().Which.Motivo.Should().Contain("101");
        classifier.Llamadas.Should().Be(0);
    }

    [Fact]
    public async Task Un_archivo_vacio_se_reporta()
    {
        var handler = Handler(Classifier(1));

        var (r, _) = await handler.HandleAsync(Matricula, [File("vacio.pdf", [])], Ct);

        r!.Errores.Should().ContainSingle().Which.Motivo.Should().Contain("vacío");
    }

    // ── Topes del lote ───────────────────────────────────────────────────────

    [Fact]
    public async Task Sin_tipos_validos_se_rechaza_la_peticion()
    {
        var (r, failure) = await Handler(Classifier(1)).HandleAsync(["compraventa"], [File("a.pdf", Pdf())], Ct);

        r.Should().BeNull();
        failure!.Status.Should().Be(400);
    }

    [Fact]
    public async Task Sin_archivos_se_rechaza_la_peticion()
    {
        var (r, failure) = await Handler(Classifier(1)).HandleAsync(Matricula, [], Ct);

        r.Should().BeNull();
        failure!.Status.Should().Be(400);
    }

    [Fact]
    public async Task Mas_archivos_del_tope_se_rechaza_el_lote_completo()
    {
        var files = Enumerable.Range(0, AnalyzeBatchHandler.MaxFiles + 1)
            .Select(i => File($"f{i}.pdf", Pdf()))
            .ToList();

        var (r, failure) = await Handler(Classifier(1)).HandleAsync(Matricula, files, Ct);

        r.Should().BeNull();
        failure!.Status.Should().Be(400);
        failure.Message.Should().Contain("21");
    }

    [Fact]
    public async Task Un_archivo_por_encima_del_tope_se_reporta_sin_tumbar_el_lote()
    {
        var grande = new byte[AnalyzeBatchHandler.MaxFileBytes + 1];
        grande[0] = 0x25; grande[1] = 0x50; grande[2] = 0x44; grande[3] = 0x46;
        var handler = Handler(Classifier(1, Doc("soat", [1])));

        var (r, failure) = await handler.HandleAsync(
            Matricula, [File("enorme.pdf", grande), File("bueno.pdf", Pdf())], Ct);

        failure.Should().BeNull();
        r!.Errores.Should().ContainSingle().Which.Filename.Should().Be("enorme.pdf");
        r.Piezas.Should().ContainSingle();
    }

    // ── ZIP ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Un_zip_se_expande_en_sus_archivos()
    {
        var zip = Zip(("soat.pdf", Pdf()), ("carpeta/impronta.pdf", Pdf()));
        var handler = Handler(Classifier(1, Doc("soat", [1])));

        var (r, _) = await handler.HandleAsync(Matricula, [File("docs.zip", zip)], Ct);

        // Las subcarpetas del comprimido se aplanan: queda el nombre del archivo.
        r!.Piezas.Select(p => p.SourceFilename).Should().BeEquivalentTo("soat.pdf", "impronta.pdf");
    }

    [Fact]
    public async Task Un_zip_ignora_los_metadatos_de_macos()
    {
        var zip = Zip(("soat.pdf", Pdf()), ("__MACOSX/._soat.pdf", [0x00, 0x01]), (".DS_Store", [0x00]));
        var handler = Handler(Classifier(1, Doc("soat", [1])));

        var (r, _) = await handler.HandleAsync(Matricula, [File("docs.zip", zip)], Ct);

        r!.Piezas.Should().ContainSingle();
        r.Errores.Should().BeEmpty();
    }

    [Fact]
    public async Task Un_zip_vacio_se_reporta()
    {
        var handler = Handler(Classifier(1));

        var (r, _) = await handler.HandleAsync(Matricula, [File("vacio.zip", Zip())], Ct);

        r!.Errores.Should().ContainSingle().Which.Motivo.Should().Contain("no trae archivos");
    }

    [Fact]
    public async Task Un_zip_dañado_se_reporta_sin_tumbar_el_lote()
    {
        var handler = Handler(Classifier(1, Doc("soat", [1])));
        byte[] roto = [0x50, 0x4B, 0x03, 0x04, 0xFF, 0xFF, 0xFF];

        var (r, failure) = await handler.HandleAsync(
            Matricula, [File("roto.zip", roto), File("bueno.pdf", Pdf())], Ct);

        failure.Should().BeNull();
        r!.Errores.Should().ContainSingle().Which.Filename.Should().Be("roto.zip");
        r.Piezas.Should().ContainSingle();
    }

    // ── Dobles ───────────────────────────────────────────────────────────────

    private static BatchInputFile File(string name, byte[] content) => new(name, content);

    private static ClassifiedDocument Doc(string tipo, int[] paginas, double confianza = 0.9) =>
        new(tipo, paginas, confianza, $"motivo {tipo}");

    private static FakeClassifier Classifier(int totalPaginas, params ClassifiedDocument[] docs) =>
        new(new BatchClassification(true, totalPaginas, docs, []));

    private static AnalyzeBatchHandler Handler(
        FakeClassifier classifier,
        int[]? noReconocidas = null,
        FakeAnalyzer? analyzer = null,
        FakeExtractor? extractor = null)
    {
        if (noReconocidas is not null)
        {
            var c = classifier.Result;
            classifier = new FakeClassifier(c with { PaginasNoReconocidas = noReconocidas });
        }
        var paginas = classifier.Result.TotalPaginas;
        return new AnalyzeBatchHandler(
            classifier,
            analyzer ?? new FakeAnalyzer(),
            extractor ?? new FakeExtractor(paginas));
    }

    private sealed class FakeClassifier(BatchClassification result) : IDocumentBatchClassifier
    {
        public BatchClassification Result { get; } = result;
        public int Llamadas { get; private set; }

        public Task<BatchClassification> ClassifyAsync(
            IReadOnlyCollection<string> tiposEsperados, ReadOnlyMemory<byte> content, string mediaType, CancellationToken ct)
        {
            Llamadas++;
            // Una imagen no tiene páginas que repartir: el clasificador real la trata como una sola.
            if (mediaType != "application/pdf")
                return Task.FromResult(Result with { TotalPaginas = 1 });
            return Task.FromResult(Result);
        }
    }

    private sealed class FakeAnalyzer(bool fails = false) : IDocumentOcrAnalyzer
    {
        public Task<DocumentOcrAnalysis> AnalyzeAsync(
            string tipo, ReadOnlyMemory<byte> content, string mediaType, CancellationToken ct) =>
            Task.FromResult(fails
                ? new DocumentOcrAnalysis(false, null, 503, "Lectura automática no disponible.")
                : new DocumentOcrAnalysis(true, new JsonObject { ["es_valido"] = true, ["tipo_documento"] = tipo }));
    }

    /// <summary>
    /// Extractor de mentira. Si no puede contar tampoco puede recortar: es el comportamiento real de
    /// PdfSharp, que abre el documento una sola vez para las dos cosas.
    /// </summary>
    private sealed class FakeExtractor(int? totalPages, bool extractFails = false) : IPdfPageExtractor
    {
        public int? CountPages(ReadOnlyMemory<byte> pdf) => totalPages;

        public byte[]? ExtractPages(ReadOnlyMemory<byte> pdf, IReadOnlyList<int> pages) =>
            extractFails || totalPages is null ? null : [0x25, 0x50, 0x44, 0x46, (byte)pages.Count];

        public byte[]? Rotate(ReadOnlyMemory<byte> pdf, int quarterTurns) => pdf.ToArray();
    }

    private static byte[] Zip(params (string Name, byte[] Content)[] entries)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in entries)
            {
                var entry = zip.CreateEntry(name);
                using var s = entry.Open();
                s.Write(content, 0, content.Length);
            }
        }
        return ms.ToArray();
    }
}
