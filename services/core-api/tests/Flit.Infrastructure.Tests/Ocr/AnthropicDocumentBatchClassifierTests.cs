using System.Net;
using System.Text;
using Flit.Infrastructure.Ocr;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Flit.Infrastructure.Tests.Ocr;

/// <summary>
/// Parseo y saneamiento de la respuesta del clasificador de lote. El modelo puede devolver tipos fuera
/// de la lista pedida, páginas imposibles o la misma página en dos documentos; nada de eso puede llegar
/// a la pantalla de revisión, así que se recorta aquí.
/// </summary>
public sealed class AnthropicDocumentBatchClassifierTests
{
    private static readonly byte[] PdfBytes = [0x25, 0x50, 0x44, 0x46];
    private static readonly string[] Matricula = ["factura", "aduana", "impronta", "soat", "rtm"];

    private static AnthropicDocumentBatchClassifier Classifier(MockHttpMessageHandler handler) =>
        new(
            new AnthropicMessagesClient(
                new HttpClient(handler) { BaseAddress = new Uri("https://anthropic.test") },
                Options.Create(new AnthropicOptions { ApiKey = "sk-ant-test" }),
                NullLogger<AnthropicMessagesClient>.Instance),
            Options.Create(new AnthropicOptions { ApiKey = "sk-ant-test" }),
            NullLogger<AnthropicDocumentBatchClassifier>.Instance);

    private static MockHttpMessageHandler Responds(string modelText) =>
        new((_, _) => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                $$"""{"content":[{"type":"text","text":{{System.Text.Json.JsonSerializer.Serialize(modelText)}}}]}""",
                Encoding.UTF8,
                "application/json"),
        });

    [Fact]
    public async Task Mapea_documentos_y_paginas_no_reconocidas()
    {
        var handler = Responds("""
            {"total_paginas":16,
             "documentos":[
               {"tipo":"aduana","paginas":[2,3,4],"confianza":0.93,"motivo":"Declaracion de importacion DIAN"},
               {"tipo":"factura","paginas":[5,6,7],"confianza":0.97,"motivo":"Factura electronica con CUFE"},
               {"tipo":"impronta","paginas":[14],"confianza":0.88,"motivo":"Certificado de improntas"}],
             "paginas_no_reconocidas":[1,8,9,10,11,12,13,15,16]}
            """);

        var r = await Classifier(handler).ClassifyAsync(
            Matricula, PdfBytes, "application/pdf", TestContext.Current.CancellationToken);

        r.Ok.Should().BeTrue();
        r.TotalPaginas.Should().Be(16);
        r.Documentos.Should().HaveCount(3);
        r.Documentos[0].Tipo.Should().Be("aduana");
        r.Documentos[0].Paginas.Should().Equal(2, 3, 4);
        r.Documentos[0].Confianza.Should().BeApproximately(0.93, 0.001);
        r.Documentos[0].Motivo.Should().Be("Declaracion de importacion DIAN");
        r.PaginasNoReconocidas.Should().Equal(1, 8, 9, 10, 11, 12, 13, 15, 16);
    }

    [Fact]
    public async Task Descarta_tipos_fuera_de_los_solicitados()
    {
        // Traspaso no lleva factura: aunque el modelo la reporte, no puede proponerse.
        var handler = Responds("""
            {"total_paginas":3,
             "documentos":[
               {"tipo":"factura","paginas":[1],"confianza":0.9},
               {"tipo":"soat","paginas":[2],"confianza":0.9}],
             "paginas_no_reconocidas":[3]}
            """);

        var r = await Classifier(handler).ClassifyAsync(
            ["impronta", "soat", "rtm"], PdfBytes, "application/pdf", TestContext.Current.CancellationToken);

        r.Documentos.Should().ContainSingle().Which.Tipo.Should().Be("soat");
    }

    [Fact]
    public async Task Una_pagina_reclamada_dos_veces_se_queda_con_el_primer_documento()
    {
        var handler = Responds("""
            {"total_paginas":2,
             "documentos":[
               {"tipo":"soat","paginas":[1,2],"confianza":0.9},
               {"tipo":"rtm","paginas":[2],"confianza":0.8}],
             "paginas_no_reconocidas":[]}
            """);

        var r = await Classifier(handler).ClassifyAsync(
            Matricula, PdfBytes, "application/pdf", TestContext.Current.CancellationToken);

        r.Documentos.Should().ContainSingle().Which.Tipo.Should().Be("soat");
        r.Documentos[0].Paginas.Should().Equal(1, 2);
    }

    [Fact]
    public async Task Descarta_paginas_fuera_de_rango_y_deduplica()
    {
        var handler = Responds("""
            {"total_paginas":3,
             "documentos":[{"tipo":"soat","paginas":[0,1,1,4,2],"confianza":0.9}],
             "paginas_no_reconocidas":[3,3,99]}
            """);

        var r = await Classifier(handler).ClassifyAsync(
            Matricula, PdfBytes, "application/pdf", TestContext.Current.CancellationToken);

        r.Documentos.Single().Paginas.Should().Equal(1, 2);
        r.PaginasNoReconocidas.Should().Equal(3);
    }

    [Fact]
    public async Task Una_pagina_ya_asignada_no_aparece_como_no_reconocida()
    {
        var handler = Responds("""
            {"total_paginas":2,
             "documentos":[{"tipo":"soat","paginas":[1],"confianza":0.9}],
             "paginas_no_reconocidas":[1,2]}
            """);

        var r = await Classifier(handler).ClassifyAsync(
            Matricula, PdfBytes, "application/pdf", TestContext.Current.CancellationToken);

        r.PaginasNoReconocidas.Should().Equal(2);
    }

    [Fact]
    public async Task Confianza_fuera_de_rango_se_recorta_a_cero_uno()
    {
        var handler = Responds("""
            {"total_paginas":2,
             "documentos":[
               {"tipo":"soat","paginas":[1],"confianza":1.7},
               {"tipo":"rtm","paginas":[2],"confianza":-0.5}],
             "paginas_no_reconocidas":[]}
            """);

        var r = await Classifier(handler).ClassifyAsync(
            Matricula, PdfBytes, "application/pdf", TestContext.Current.CancellationToken);

        r.Documentos[0].Confianza.Should().Be(1d);
        r.Documentos[1].Confianza.Should().Be(0d);
    }

    [Fact]
    public async Task Un_archivo_sin_nada_aprovechable_es_exito_no_error()
    {
        var handler = Responds("""{"total_paginas":4,"documentos":[],"paginas_no_reconocidas":[1,2,3,4]}""");

        var r = await Classifier(handler).ClassifyAsync(
            Matricula, PdfBytes, "application/pdf", TestContext.Current.CancellationToken);

        r.Ok.Should().BeTrue();
        r.Documentos.Should().BeEmpty();
        r.PaginasNoReconocidas.Should().HaveCount(4);
    }

    [Fact]
    public async Task Quita_los_fences_de_markdown_alrededor_del_json()
    {
        var handler = Responds("```json\n{\"total_paginas\":1,\"documentos\":[{\"tipo\":\"soat\",\"paginas\":[1],\"confianza\":0.9}],\"paginas_no_reconocidas\":[]}\n```");

        var r = await Classifier(handler).ClassifyAsync(
            Matricula, PdfBytes, "application/pdf", TestContext.Current.CancellationToken);

        r.Ok.Should().BeTrue();
        r.Documentos.Should().ContainSingle();
    }

    [Fact]
    public async Task Respuesta_no_interpretable_degrada_a_500()
    {
        var handler = Responds("no soy json");

        var r = await Classifier(handler).ClassifyAsync(
            Matricula, PdfBytes, "application/pdf", TestContext.Current.CancellationToken);

        r.Ok.Should().BeFalse();
        r.Status.Should().Be(500);
        r.Message.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Proveedor_caido_degrada_a_503_con_mensaje_para_el_operador()
    {
        var handler = new MockHttpMessageHandler(
            (_, _) => new HttpResponseMessage(HttpStatusCode.InternalServerError));

        var r = await Classifier(handler).ClassifyAsync(
            Matricula, PdfBytes, "application/pdf", TestContext.Current.CancellationToken);

        r.Ok.Should().BeFalse();
        r.Status.Should().Be(503);
        r.Message.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Sin_tipos_esperados_no_llama_al_modelo()
    {
        var llamadas = 0;
        var handler = new MockHttpMessageHandler((_, _) =>
        {
            llamadas++;
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        var r = await Classifier(handler).ClassifyAsync(
            ["compraventa"], PdfBytes, "application/pdf", TestContext.Current.CancellationToken);

        r.Ok.Should().BeFalse();
        r.Status.Should().Be(400);
        llamadas.Should().Be(0);
    }

    [Fact]
    public async Task Usa_el_modelo_y_el_tope_de_salida_del_clasificador()
    {
        string? body = null;
        var handler = new MockHttpMessageHandler((req, _) =>
        {
            body = req.Content!.ReadAsStringAsync(TestContext.Current.CancellationToken).GetAwaiter().GetResult();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"content":[{"type":"text","text":"{\"total_paginas\":1,\"documentos\":[],\"paginas_no_reconocidas\":[1]}"}]}""",
                    Encoding.UTF8,
                    "application/json"),
            };
        });

        await Classifier(handler).ClassifyAsync(
            Matricula, PdfBytes, "application/pdf", TestContext.Current.CancellationToken);

        // El clasificador NO puede caer al modelo del analizador por tipo ni a su tope de 2000 tokens:
        // con Sonnet el thinking cuenta contra max_tokens y un tope corto trunca el JSON.
        body.Should().Contain("claude-sonnet-5");
        body.Should().Contain("\"max_tokens\":8000");
    }

    private sealed class MockHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> responder)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(responder(request, cancellationToken));
    }
}
