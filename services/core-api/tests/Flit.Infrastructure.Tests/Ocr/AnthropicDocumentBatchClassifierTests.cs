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

    /// <summary>
    /// Cuerpo SSE equivalente a lo que devuelve Anthropic en streaming. El texto se parte en dos
    /// deltas a propósito: si el cliente se quedara con el primer bloque en vez de concatenar, estos
    /// tests lo cazarían.
    /// </summary>
    private static string Sse(string modelText)
    {
        var corte = modelText.Length / 2;
        var sb = new StringBuilder();
        sb.Append("event: message_start\ndata: {\"type\":\"message_start\",\"message\":{\"usage\":{}}}\n\n");
        foreach (var trozo in new[] { modelText[..corte], modelText[corte..] })
        {
            sb.Append("event: content_block_delta\ndata: ")
              .Append("{\"type\":\"content_block_delta\",\"index\":0,\"delta\":{\"type\":\"text_delta\",\"text\":")
              .Append(System.Text.Json.JsonSerializer.Serialize(trozo))
              .Append("}}\n\n");
        }
        sb.Append("event: message_delta\ndata: {\"type\":\"message_delta\",\"delta\":{\"stop_reason\":\"end_turn\"}}\n\n");
        sb.Append("event: message_stop\ndata: {\"type\":\"message_stop\"}\n\n");
        return sb.ToString();
    }

    private static HttpResponseMessage SseResponse(string modelText) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(Sse(modelText), Encoding.UTF8, "text/event-stream"),
        };

    private static MockHttpMessageHandler Responds(string modelText) =>
        new((_, _) => SseResponse(modelText));

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
            return SseResponse("""{"total_paginas":1,"documentos":[],"paginas_no_reconocidas":[1]}""");
        });

        await Classifier(handler).ClassifyAsync(
            Matricula, PdfBytes, "application/pdf", TestContext.Current.CancellationToken);

        // El clasificador NO puede caer al modelo del analizador por tipo ni a su tope de 2000 tokens:
        // con Sonnet el thinking cuenta contra max_tokens y un tope corto trunca el JSON.
        body.Should().Contain("claude-sonnet-5");
        body.Should().Contain("\"max_tokens\":8000");
        // Sin streaming, un expediente pesado muere por «connection reset» antes de responder.
        body.Should().Contain("\"stream\":true");
    }

    [Fact]
    public async Task Concatena_los_deltas_de_texto_del_stream()
    {
        // El JSON llega partido en varios `text_delta`; quedarse con uno solo daría JSON invalido.
        var handler = Responds("""
            {"total_paginas":9,
             "documentos":[{"tipo":"aduana","paginas":[2,3,4,5],"confianza":0.9,"motivo":"Declaracion de importacion de lote"}],
             "paginas_no_reconocidas":[1,6,7,8,9]}
            """);

        var r = await Classifier(handler).ClassifyAsync(
            Matricula, PdfBytes, "application/pdf", TestContext.Current.CancellationToken);

        r.Ok.Should().BeTrue();
        r.TotalPaginas.Should().Be(9);
        r.Documentos.Should().ContainSingle().Which.Paginas.Should().Equal(2, 3, 4, 5);
    }

    [Fact]
    public async Task Un_error_a_mitad_del_stream_degrada_a_503()
    {
        // El proveedor responde 200 y falla despues, dentro del SSE. Sin tratarlo, el fallo se
        // confundiria con una respuesta vacia.
        var handler = new MockHttpMessageHandler((_, _) => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                "event: content_block_delta\ndata: {\"type\":\"content_block_delta\",\"index\":0,\"delta\":{\"type\":\"text_delta\",\"text\":\"{\\\"total\"}}\n\n"
                + "event: error\ndata: {\"type\":\"error\",\"error\":{\"type\":\"overloaded_error\"}}\n\n",
                Encoding.UTF8,
                "text/event-stream"),
        });

        var r = await Classifier(handler).ClassifyAsync(
            Matricula, PdfBytes, "application/pdf", TestContext.Current.CancellationToken);

        r.Ok.Should().BeFalse();
        r.Status.Should().Be(503);
        r.Message.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Rescata_el_json_aunque_el_modelo_escriba_prosa_alrededor()
    {
        // Medido en produccion: ante un documento que no encaja del todo en el esquema, el modelo
        // cierra el JSON y añade un parrafo explicandolo. Antes eso costaba la clasificacion entera.
        var handler = Responds(
            "Analicé el expediente y encontré lo siguiente:\n\n"
            + """{"total_paginas":2,"documentos":[{"tipo":"soat","paginas":[1],"confianza":0.9}],"paginas_no_reconocidas":[2]}"""
            + "\n\n**Nota:** la página 2 es una hoja de firmas y no corresponde a ningún tipo solicitado.");

        var r = await Classifier(handler).ClassifyAsync(
            Matricula, PdfBytes, "application/pdf", TestContext.Current.CancellationToken);

        r.Ok.Should().BeTrue();
        r.Documentos.Should().ContainSingle().Which.Tipo.Should().Be("soat");
        r.PaginasNoReconocidas.Should().Equal(2);
    }

    private sealed class MockHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> responder)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(responder(request, cancellationToken));
    }
}
