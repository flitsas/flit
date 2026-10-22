using System.Net;
using System.Text;
using System.Text.Json;
using Flit.DrFlit.Application.Abstractions;
using Flit.DrFlit.Application.Chat;
using Flit.Infrastructure.DrFlit;
using Flit.Infrastructure.Ocr;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Flit.Infrastructure.Tests.DrFlit;

/// <summary>
/// HU #12918 — adaptador del chat sobre Anthropic. AC1: modelo, tope y deadline salen de
/// <c>Anthropic:DrFlit*</c>. AC4: con el LLM apagado no se hace ninguna llamada HTTP.
/// </summary>
public sealed class AnthropicDrFlitChatModelTests
{
    private static readonly DrFlitSystemPrompt Prompt = new("MANUAL", "INSTRUCCIONES");

    private static readonly DrFlitTurn[] Turns =
    [
        new(DrFlitTurnRole.User, "hola"),
        new(DrFlitTurnRole.Assistant, "¿en qué te ayudo?"),
        new(DrFlitTurnRole.User, "¿cómo subsano?"),
    ];

    private const string Body = """
        {"content":[{"type":"text","text":"{\"intent\":\"duda\",\"reply\":\"Así.\",\"citedSlugs\":[]}"}],
         "stop_reason":"end_turn",
         "usage":{"input_tokens":10,"output_tokens":20,"cache_read_input_tokens":30,"cache_creation_input_tokens":40}}
        """;

    private static AnthropicDrFlitChatModel Model(MockHttpMessageHandler handler, AnthropicOptions options)
    {
        var wrapped = Options.Create(options);
        return new(
            new AnthropicMessagesClient(
                new HttpClient(handler) { BaseAddress = new Uri("https://anthropic.test") },
                wrapped,
                NullLogger<AnthropicMessagesClient>.Instance),
            wrapped);
    }

    private static MockHttpMessageHandler Capturing(Action<string> onBody, Func<HttpResponseMessage>? response = null) =>
        new(async (req, ct) =>
        {
            onBody(await req.Content!.ReadAsStringAsync(ct));
            return response?.Invoke()
                ?? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Body, Encoding.UTF8, "application/json") };
        });

    [Fact]
    public async Task AC1_UsaModeloYTopeDeLaConfiguracionDrFlit()
    {
        string? sent = null;
        var options = new AnthropicOptions
        {
            ApiKey = "sk-ant-test",
            Model = "modelo-del-ocr",
            MaxTokens = 2000,
            DrFlitModel = "modelo-del-chat",
            DrFlitMaxTokens = 432,
        };

        await Model(Capturing(b => sent = b), options)
            .CompleteAsync(Prompt, Turns, TestContext.Current.CancellationToken);

        using var doc = JsonDocument.Parse(sent!);
        doc.RootElement.GetProperty("model").GetString().Should().Be("modelo-del-chat");
        doc.RootElement.GetProperty("max_tokens").GetInt32().Should().Be(432);
    }

    [Fact]
    public async Task AC1_ManualPrimeroEInstruccionesDespues_AmbosCacheables()
    {
        string? sent = null;

        await Model(Capturing(b => sent = b), new AnthropicOptions { ApiKey = "sk-ant-test" })
            .CompleteAsync(Prompt, Turns, TestContext.Current.CancellationToken);

        using var doc = JsonDocument.Parse(sent!);
        var system = doc.RootElement.GetProperty("system").EnumerateArray().ToList();
        system.Select(b => b.GetProperty("text").GetString()).Should().Equal("MANUAL", "INSTRUCCIONES");
        system.Should().AllSatisfy(b => b.TryGetProperty("cache_control", out _).Should().BeTrue());
        doc.RootElement.GetProperty("messages").EnumerateArray()
            .Select(m => m.GetProperty("role").GetString())
            .Should().Equal("user", "assistant", "user");
    }

    [Fact]
    public async Task AC1_RespuestaOk_TraeTextoYUso()
    {
        var result = await Model(Capturing(_ => { }), new AnthropicOptions { ApiKey = "sk-ant-test" })
            .CompleteAsync(Prompt, Turns, TestContext.Current.CancellationToken);

        result.Status.Should().Be(DrFlitModelCallStatus.Ok);
        result.Text.Should().Contain("\"intent\":\"duda\"");
        result.Usage.Should().Be(new DrFlitTokenUsage(10, 20, 30, 40));
    }

    [Fact]
    public async Task AC4_LlmApagado_NoLlamaYDevuelveDisabled()
    {
        var calls = 0;

        var result = await Model(Capturing(_ => calls++), new AnthropicOptions { ApiKey = "sk-ant-test", DrFlitEnabled = false })
            .CompleteAsync(Prompt, Turns, TestContext.Current.CancellationToken);

        result.Status.Should().Be(DrFlitModelCallStatus.Disabled);
        calls.Should().Be(0);
    }

    [Fact]
    public async Task SinApiKey_NoLlamaYDevuelveDisabled()
    {
        var calls = 0;

        var result = await Model(Capturing(_ => calls++), new AnthropicOptions { ApiKey = "" })
            .CompleteAsync(Prompt, Turns, TestContext.Current.CancellationToken);

        result.Status.Should().Be(DrFlitModelCallStatus.Disabled);
        calls.Should().Be(0);
    }

    [Fact]
    public async Task ProveedorFalla_DevuelveFailed()
    {
        var result = await Model(
                Capturing(_ => { }, () => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)),
                new AnthropicOptions { ApiKey = "sk-ant-test" })
            .CompleteAsync(Prompt, Turns, TestContext.Current.CancellationToken);

        result.Status.Should().Be(DrFlitModelCallStatus.Failed);
        result.Text.Should().BeNull();
    }

    private sealed class MockHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            responder(request, cancellationToken);
    }
}
