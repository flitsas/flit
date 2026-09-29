using System.Net;
using System.Text;
using System.Text.Json;
using Flit.Infrastructure.Ocr;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Flit.Infrastructure.Tests.Ocr;

/// <summary>
/// HU #12918 — llamada de chat de DR. FLIT sobre el cliente Anthropic existente: payload sin streaming ni
/// tools, <c>system</c> en bloques cacheables, lectura del texto y del uso de tokens, y la misma política
/// de reintento que la llamada de visión.
/// </summary>
public sealed class AnthropicMessagesClientSendChatAsyncTests
{
    private static readonly AnthropicSystemBlock[] SystemBlocks =
    [
        new("MANUAL DE FLIT", Cache: true),
        new("INSTRUCCIONES", Cache: true),
    ];

    private static readonly AnthropicChatTurn[] Turns =
    [
        new("user", "¿Cómo creo un trámite?"),
        new("assistant", "¿De qué tipo?"),
        new("user", "Matrícula inicial"),
    ];

    private const string ModelText = """{"intent":"duda","reply":"Así.","citedSlugs":["crear-tramite"]}""";

    private static AnthropicMessagesClient Client(MockHttpMessageHandler handler, string apiKey = "sk-ant-test") =>
        new(
            new HttpClient(handler) { BaseAddress = new Uri("https://anthropic.test") },
            Options.Create(new AnthropicOptions { ApiKey = apiKey }),
            NullLogger<AnthropicMessagesClient>.Instance);

    private static Task<AnthropicChatResult> Send(AnthropicMessagesClient client, int timeoutSeconds = 20) =>
        client.SendChatAsync(SystemBlocks, Turns, "modelo-de-config", 321, timeoutSeconds, TestContext.Current.CancellationToken);

    private static HttpResponseMessage Ok(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static string ResponseBody(string text) => $$$"""
        {"id":"msg_1","type":"message","role":"assistant",
         "content":[{"type":"text","text":{{{JsonSerializer.Serialize(text)}}}}],
         "stop_reason":"end_turn",
         "usage":{"input_tokens":120,"output_tokens":45,"cache_read_input_tokens":38000,"cache_creation_input_tokens":0}}
        """;

    // ── AC1 — la llamada usa lo que llega de configuración ──────────────────────────────

    [Fact]
    public async Task Payload_UsaModeloYTopeRecibidos_SinStreamingNiTools()
    {
        string? sent = null;
        var handler = new MockHttpMessageHandler(async (req, ct) =>
        {
            sent = await req.Content!.ReadAsStringAsync(ct);
            return Ok(ResponseBody(ModelText));
        });

        await Send(Client(handler));

        using var doc = JsonDocument.Parse(sent!);
        var root = doc.RootElement;
        root.GetProperty("model").GetString().Should().Be("modelo-de-config");
        root.GetProperty("max_tokens").GetInt32().Should().Be(321);
        root.TryGetProperty("stream", out _).Should().BeFalse();
        // AC2 — sin tool-use: el modelo no tiene nada que invocar.
        root.TryGetProperty("tools", out _).Should().BeFalse();
        root.TryGetProperty("tool_choice", out _).Should().BeFalse();
    }

    [Fact]
    public async Task Payload_SystemEnBloquesCacheables_ConversacionEnMessages()
    {
        string? sent = null;
        var handler = new MockHttpMessageHandler(async (req, ct) =>
        {
            sent = await req.Content!.ReadAsStringAsync(ct);
            return Ok(ResponseBody(ModelText));
        });

        await Send(Client(handler));

        using var doc = JsonDocument.Parse(sent!);
        var system = doc.RootElement.GetProperty("system").EnumerateArray().ToList();
        system.Select(b => b.GetProperty("text").GetString()).Should().Equal("MANUAL DE FLIT", "INSTRUCCIONES");
        system.Should().AllSatisfy(b =>
            b.GetProperty("cache_control").GetProperty("type").GetString().Should().Be("ephemeral"));

        // El texto del usuario viaja como turno, nunca dentro de system.
        var messages = doc.RootElement.GetProperty("messages").EnumerateArray().ToList();
        messages.Select(m => m.GetProperty("role").GetString()).Should().Equal("user", "assistant", "user");
        messages[2].GetProperty("content").GetString().Should().Be("Matrícula inicial");
    }

    [Fact]
    public async Task BloqueSinCache_NoLlevaCacheControl()
    {
        string? sent = null;
        var handler = new MockHttpMessageHandler(async (req, ct) =>
        {
            sent = await req.Content!.ReadAsStringAsync(ct);
            return Ok(ResponseBody(ModelText));
        });

        await Client(handler).SendChatAsync(
            [new AnthropicSystemBlock("solo texto", Cache: false)], Turns, "m", 10, 20,
            TestContext.Current.CancellationToken);

        using var doc = JsonDocument.Parse(sent!);
        doc.RootElement.GetProperty("system")[0].TryGetProperty("cache_control", out _).Should().BeFalse();
    }

    [Fact]
    public async Task Headers_LlevanApiKeyYVersion()
    {
        HttpRequestMessage? captured = null;
        var handler = new MockHttpMessageHandler((req, _) =>
        {
            captured = req;
            return Task.FromResult(Ok(ResponseBody(ModelText)));
        });

        await Send(Client(handler));

        captured!.RequestUri!.AbsolutePath.Should().Be("/v1/messages");
        captured.Headers.GetValues("x-api-key").Should().Equal("sk-ant-test");
        captured.Headers.GetValues("anthropic-version").Should().Equal("2023-06-01");
    }

    [Fact]
    public async Task Respuesta200_DevuelveTextoYUsoDeTokens()
    {
        var handler = new MockHttpMessageHandler((_, _) => Task.FromResult(Ok(ResponseBody(ModelText))));

        var result = await Send(Client(handler));

        result.Ok.Should().BeTrue();
        result.Text.Should().Be(ModelText);
        result.Usage.Should().Be(new AnthropicUsage(120, 45, 38000, 0));
    }

    [Fact]
    public async Task VariosBloquesDeTexto_SeConcatenan()
    {
        const string body = """
            {"content":[{"type":"text","text":"{\"intent\":"},{"type":"text","text":"\"soporte\"}"}],"stop_reason":"end_turn"}
            """;
        var handler = new MockHttpMessageHandler((_, _) => Task.FromResult(Ok(body)));

        var result = await Send(Client(handler));

        result.Text.Should().Be("""{"intent":"soporte"}""");
        result.Usage.Should().BeNull();
    }

    // ── Degradación ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SinApiKey_NoLlamaYFalla()
    {
        var calls = 0;
        var handler = new MockHttpMessageHandler((_, _) =>
        {
            calls++;
            return Task.FromResult(Ok(ResponseBody(ModelText)));
        });

        var result = await Send(Client(handler, apiKey: ""));

        result.Ok.Should().BeFalse();
        calls.Should().Be(0);
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadRequest)]
    public async Task RespuestaNo200_FallaSinReintentar(HttpStatusCode status)
    {
        var calls = 0;
        var handler = new MockHttpMessageHandler((_, _) =>
        {
            calls++;
            return Task.FromResult(new HttpResponseMessage(status));
        });

        var result = await Send(Client(handler));

        result.Ok.Should().BeFalse();
        calls.Should().Be(1);
    }

    [Fact]
    public async Task ErrorDeTransporte_ReintentaUnaVezYRecupera()
    {
        var calls = 0;
        var handler = new MockHttpMessageHandler((_, _) =>
        {
            calls++;
            return calls == 1
                ? throw new HttpRequestException("connection reset")
                : Task.FromResult(Ok(ResponseBody(ModelText)));
        });

        var result = await Send(Client(handler));

        result.Ok.Should().BeTrue();
        calls.Should().Be(2);
    }

    [Fact]
    public async Task Timeout_EnAmbosIntentos_Falla()
    {
        var calls = 0;
        var handler = new MockHttpMessageHandler(async (_, ct) =>
        {
            calls++;
            await Task.Delay(Timeout.Infinite, ct);
            return Ok(ResponseBody(ModelText));
        });

        var result = await Send(Client(handler), timeoutSeconds: 1);

        result.Ok.Should().BeFalse();
        calls.Should().Be(2);
    }

    [Theory]
    [InlineData("no es json")]
    [InlineData("""{"content":[],"stop_reason":"end_turn"}""")]
    [InlineData("""{"content":[{"type":"text","text":"   "}]}""")]
    public async Task RespuestaVaciaONoInterpretable_Falla(string body)
    {
        var handler = new MockHttpMessageHandler((_, _) => Task.FromResult(Ok(body)));

        (await Send(Client(handler))).Ok.Should().BeFalse();
    }

    private sealed class MockHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            responder(request, cancellationToken);
    }
}
