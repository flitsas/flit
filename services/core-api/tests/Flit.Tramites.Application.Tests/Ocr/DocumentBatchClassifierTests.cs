using Flit.Tramites.Application.Ocr;
using FluentAssertions;
using Xunit;

namespace Flit.Tramites.Application.Tests.Ocr;

/// <summary>
/// Prompt de clasificación del cargue masivo + mock determinista. Lo que se protege aquí es el contrato
/// que consume el handler de lote: el prompt sólo ofrece los tipos que el trámite espera, y el mock
/// recorre las dos ramas de la pantalla de revisión (piezas propuestas y no reconocidos).
/// </summary>
public sealed class DocumentBatchClassifierTests
{
    private static readonly byte[] PdfBytes = [0x25, 0x50, 0x44, 0x46];
    private static readonly byte[] JpgBytes = [0xFF, 0xD8];
    private readonly MockDocumentBatchClassifier _mock = new();

    private static readonly string[] Matricula = ["factura", "aduana", "impronta", "soat", "rtm"];
    private static readonly string[] Traspaso = ["impronta", "soat", "rtm"];

    // ── Prompt ───────────────────────────────────────────────────────────────

    [Fact]
    public void Prompt_lista_solo_los_tipos_pedidos()
    {
        var prompt = DocumentOcrPrompts.ClassificationPrompt(Traspaso);

        prompt.Should().Contain("impronta, soat, rtm");
        // Traspaso no lleva factura ni aduana: no deben aparecer como tipos a identificar.
        prompt.Should().NotContain("TIPOS QUE DEBES IDENTIFICAR (y SOLO estos): factura");
    }

    [Fact]
    public void Prompt_descarta_tipos_sin_soporte_ocr()
    {
        var prompt = DocumentOcrPrompts.ClassificationPrompt(["soat", "compraventa", "cedulas"]);

        prompt.Should().Contain("TIPOS QUE DEBES IDENTIFICAR (y SOLO estos): soat\n");
    }

    [Fact]
    public void Prompt_pide_el_contrato_json_que_parsea_el_clasificador()
    {
        var prompt = DocumentOcrPrompts.ClassificationPrompt(Matricula);

        prompt.Should().Contain("total_paginas");
        prompt.Should().Contain("documentos");
        prompt.Should().Contain("paginas_no_reconocidas");
        prompt.Should().Contain("confianza");
    }

    // ── Mock ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Mock_reparte_una_pagina_por_tipo_esperado()
    {
        var r = await _mock.ClassifyAsync(Matricula, PdfBytes, "application/pdf", TestContext.Current.CancellationToken);

        r.Ok.Should().BeTrue();
        r.Documentos.Should().HaveCount(5);
        r.Documentos.Select(d => d.Tipo).Should().Equal(Matricula);
        r.Documentos.Select(d => d.Paginas.Single()).Should().Equal(1, 2, 3, 4, 5);
    }

    [Fact]
    public async Task Mock_deja_una_pagina_sin_reconocer_para_ejercitar_la_bandeja()
    {
        var r = await _mock.ClassifyAsync(Traspaso, PdfBytes, "application/pdf", TestContext.Current.CancellationToken);

        r.TotalPaginas.Should().Be(4);
        r.PaginasNoReconocidas.Should().Equal(4);
    }

    [Fact]
    public async Task Mock_ignora_tipos_sin_prompt_ocr()
    {
        var r = await _mock.ClassifyAsync(
            ["soat", "compraventa"], PdfBytes, "application/pdf", TestContext.Current.CancellationToken);

        r.Documentos.Should().ContainSingle().Which.Tipo.Should().Be("soat");
    }

    [Fact]
    public async Task Mock_no_parte_una_imagen()
    {
        var r = await _mock.ClassifyAsync(
            Matricula, JpgBytes, "image/jpeg", TestContext.Current.CancellationToken);

        r.TotalPaginas.Should().Be(1);
        r.PaginasNoReconocidas.Should().BeEmpty();
        r.Documentos.Should().ContainSingle().Which.Paginas.Should().Equal(1);
    }

    [Fact]
    public async Task Mock_sin_tipos_esperados_no_propone_nada()
    {
        var r = await _mock.ClassifyAsync([], PdfBytes, "application/pdf", TestContext.Current.CancellationToken);

        r.Ok.Should().BeTrue();
        r.Documentos.Should().BeEmpty();
    }
}
