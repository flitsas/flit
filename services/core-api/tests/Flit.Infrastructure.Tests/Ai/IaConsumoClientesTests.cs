using System.Net;
using System.Text;
using Flit.Admin.Application.Auditing;
using Flit.Admin.Application.Plataforma.Ai;
using Flit.Admin.Domain.Plataforma.Ai;
using Flit.Infrastructure.Ai.Providers;
using Flit.Infrastructure.Ai.Routing;
using Flit.Infrastructure.Ocr;
using Flit.Platform.Sdk.IaConsumo;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Flit.Infrastructure.Tests.Ai;

/// <summary>
/// ADR-0090 — los clientes HTTP de IA dejan una llamada registrada por CADA intento HTTP (éxito, 429, 5xx, timeout),
/// con lo que el proveedor devolvió (id, modelo, tokens, stop_reason) y el ámbito vigente; y el router marca la pasada
/// de cada proveedor de la cola. El registro nunca cambia lo que devuelve el cliente.
/// </summary>
public sealed class IaConsumoClientesTests
{
    /// <summary>Registrador que guarda las llamadas y el ámbito que había en el momento de registrar.</summary>
    private sealed class RegistradorFalso : IIaConsumoRegistrador
    {
        public List<(IaLlamada Llamada, IaAmbitoActual? Ambito)> Llamadas { get; } = [];
        public IIaCallContext Contexto => IaCallContext.Instancia;
        public bool Activo => true;
        public bool DetalleActivo { get; init; }

        public void Registrar(IaLlamada llamada)
        {
            lock (Llamadas)
                Llamadas.Add((llamada, Contexto.Actual));
        }
    }

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder) : HttpMessageHandler
    {
        public int Llamadas { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Llamadas++;
            return responder(request, cancellationToken);
        }
    }

    private static Handler Responde(Func<HttpResponseMessage> respuesta) => new((_, _) => Task.FromResult(respuesta()));

    private static AnthropicMessagesClient Anthropic(Handler handler, IIaConsumoRegistrador registrador) =>
        new(
            new HttpClient(handler) { BaseAddress = new Uri("https://anthropic.test") },
            Options.Create(new AnthropicOptions { ApiKey = "sk-ant-test", Model = "claude-haiku-4-5", MaxTokens = 2000 }),
            NullLogger<AnthropicMessagesClient>.Instance,
            registrador);

    private const string Sse = """
        event: message_start
        data: {"type":"message_start","message":{"id":"msg_01","model":"claude-haiku-4-5-20251001","usage":{"input_tokens":3300,"cache_read_input_tokens":0,"cache_creation_input_tokens":0,"output_tokens":1}}}

        event: content_block_delta
        data: {"type":"content_block_delta","index":0,"delta":{"type":"text_delta","text":"{\"derecha\":true}"}}

        event: message_delta
        data: {"type":"message_delta","delta":{"stop_reason":"end_turn"},"usage":{"output_tokens":12}}

        event: message_stop
        data: {"type":"message_stop"}


        """;

    private static HttpResponseMessage SseOk() =>
        new(HttpStatusCode.OK) { Content = new StringContent(Sse, Encoding.UTF8, "text/event-stream") };

    private static HttpResponseMessage Error(HttpStatusCode status, string tipo) =>
        new(status)
        {
            Content = new StringContent($$$"""{"type":"error","error":{"type":"{{{tipo}}}","message":"detalle del proveedor"}}""",
                Encoding.UTF8, "application/json"),
        };

    private static Task<AnthropicVisionResult> Vision(AnthropicMessagesClient client, int? timeout = null) =>
        client.SendVisionAsync(Convert.ToBase64String(new byte[300]), "application/pdf", "PROMPT", TestContext.Current.CancellationToken,
            timeoutSeconds: timeout);

    [Fact]
    public async Task VisionExitosa_RegistraIdModeloTokensYElAmbitoDeQuienLlama()
    {
        var registrador = new RegistradorFalso();
        var client = Anthropic(Responde(SseOk), registrador);

        AnthropicVisionResult r;
        using (IaCallContext.Instancia.Iniciar(IaProcesosFlit.ModuloTramites, IaProcesosFlit.OrientacionPagina, IaTiposTarea.Orientacion))
            r = await Vision(client);

        r.Ok.Should().BeTrue("el registro no cambia lo que devuelve el cliente");
        var (llamada, ambito) = registrador.Llamadas.Single();
        llamada.Proveedor.Should().Be("anthropic");
        llamada.Resultado.Should().Be(IaResultados.Exito);
        llamada.HttpStatus.Should().Be(200);
        llamada.Intento.Should().Be((short)1);
        llamada.IdRespuestaProveedor.Should().Be("msg_01");
        llamada.ModeloSolicitado.Should().Be("claude-haiku-4-5");
        llamada.ModeloRespondido.Should().Be("claude-haiku-4-5-20251001");
        llamada.TokensEntrada.Should().Be(3300);
        llamada.TokensSalida.Should().Be(12, "el usage del message_delta es el acumulado final");
        llamada.StopReason.Should().Be("end_turn");
        llamada.MaxTokens.Should().Be(2000);
        llamada.ArchivoMime.Should().Be("application/pdf");
        llamada.ArchivoBytes.Should().Be(300);
        llamada.PromptTexto.Should().Be("PROMPT");
        llamada.RespuestaTexto.Should().BeNull("sin detalle no se guarda la respuesta");
        ambito!.Ambito.Proceso.Should().Be(IaProcesosFlit.OrientacionPagina);
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests, "rate_limit_error", IaResultados.RateLimit)]
    [InlineData(HttpStatusCode.InternalServerError, "api_error", IaResultados.ErrorProveedor)]
    [InlineData((HttpStatusCode)529, "overloaded_error", IaResultados.ErrorProveedor)]
    [InlineData(HttpStatusCode.BadRequest, "invalid_request_error", IaResultados.Rechazado)]
    public async Task RespuestaNo200_RegistraUnSoloIntentoConElCodigoDelProveedor(HttpStatusCode status, string tipo, string resultado)
    {
        var registrador = new RegistradorFalso();
        var handler = Responde(() => Error(status, tipo));

        var r = await Vision(Anthropic(handler, registrador));

        r.Ok.Should().BeFalse();
        r.Status.Should().Be(503);
        handler.Llamadas.Should().Be(1, "una respuesta HTTP completa no se reintenta");
        var llamada = registrador.Llamadas.Single().Llamada;
        llamada.Resultado.Should().Be(resultado);
        llamada.HttpStatus.Should().Be((int)status);
        llamada.ErrorCodigo.Should().Be(tipo);
        llamada.ErrorMensaje.Should().Be("detalle del proveedor");
    }

    [Fact]
    public async Task Timeout_RegistraCadaIntentoConLaMismaCorrelacion()
    {
        var registrador = new RegistradorFalso();
        var handler = new Handler(async (_, ct) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(10), ct);
            return SseOk();
        });

        var r = await Vision(Anthropic(handler, registrador), timeout: 1);

        r.Ok.Should().BeFalse();
        registrador.Llamadas.Should().HaveCount(2);
        registrador.Llamadas.Select(l => l.Llamada.Intento).Should().Equal((short)1, (short)2);
        registrador.Llamadas.Should().OnlyContain(l => l.Llamada.Resultado == IaResultados.Timeout && l.Llamada.HttpStatus == 0);
        registrador.Llamadas.Select(l => l.Ambito!.CorrelacionId).Distinct().Should().ContainSingle(
            "los dos intentos son la misma operación aunque nadie haya abierto un ámbito afuera");
    }

    [Fact]
    public async Task ErrorDeRed_RegistraRedYReintenta()
    {
        var registrador = new RegistradorFalso();
        var intentos = 0;
        var handler = new Handler((_, _) =>
            ++intentos == 1 ? throw new HttpRequestException("conexión rechazada") : Task.FromResult(SseOk()));

        var r = await Vision(Anthropic(handler, registrador));

        r.Ok.Should().BeTrue();
        registrador.Llamadas.Select(l => l.Llamada.Resultado).Should().Equal(IaResultados.Red, IaResultados.Exito);
    }

    [Fact]
    public async Task SinApiKey_RegistraNoConfiguradoSinLlamarAlProveedor()
    {
        var registrador = new RegistradorFalso();
        var handler = Responde(SseOk);
        var client = new AnthropicMessagesClient(
            new HttpClient(handler) { BaseAddress = new Uri("https://anthropic.test") },
            Options.Create(new AnthropicOptions { ApiKey = "" }),
            NullLogger<AnthropicMessagesClient>.Instance,
            registrador);

        await Vision(client);

        handler.Llamadas.Should().Be(0);
        registrador.Llamadas.Single().Llamada.Resultado.Should().Be(IaResultados.NoConfigurado);
    }

    [Fact]
    public async Task Chat_RegistraTokensDeCacheYConDetalleLaConversacion()
    {
        var registrador = new RegistradorFalso { DetalleActivo = true };
        var handler = Responde(() => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""
                {"id":"msg_9","model":"claude-haiku-4-5","content":[{"type":"text","text":"Hola"}],"stop_reason":"end_turn",
                 "usage":{"input_tokens":120,"output_tokens":45,"cache_read_input_tokens":38000,"cache_creation_input_tokens":10}}
                """, Encoding.UTF8, "application/json"),
        });

        var r = await Anthropic(handler, registrador).SendChatAsync(
            [new AnthropicSystemBlock("MANUAL", Cache: true)], [new AnthropicChatTurn("user", "¿Cómo?")],
            "claude-haiku-4-5", 600, 20, TestContext.Current.CancellationToken);

        r.Ok.Should().BeTrue();
        var llamada = registrador.Llamadas.Single().Llamada;
        llamada.TipoTareaPorDefecto.Should().Be(IaTiposTarea.Chat);
        llamada.TokensCacheLectura.Should().Be(38000);
        llamada.TokensCacheEscritura.Should().Be(10);
        llamada.IdRespuestaProveedor.Should().Be("msg_9");
        llamada.PromptBase.Should().Be("MANUAL", "el hash identifica el system, no cambia con cada turno");
        llamada.PromptTexto.Should().Contain("¿Cómo?");
        llamada.RespuestaTexto.Should().Be("Hola");
    }

    [Fact]
    public async Task OpenAiCompatible_SeparaLosTokensCacheadosYRegistraElProveedor()
    {
        var registrador = new RegistradorFalso();
        var handler = Responde(() => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""
                {"id":"chatcmpl-1","model":"moonshot-v1-8k","choices":[{"message":{"content":"ok"},"finish_reason":"stop"}],
                 "usage":{"prompt_tokens":1000,"completion_tokens":20,"prompt_tokens_details":{"cached_tokens":600}}}
                """, Encoding.UTF8, "application/json"),
        });
        var client = new OpenAiCompatibleClient(new HttpClient(handler), NullLogger<OpenAiCompatibleClient>.Instance, registrador);

        var r = await client.SendChatAsync(
            new KimiProviderOptions { ApiKey = "k", BaseUrl = "https://kimi.test/v1", Model = "moonshot-v1-8k" },
            [new AiChatSystemBlock("S", Cache: false)], [new AiChatTurn("user", "hola")], TestContext.Current.CancellationToken);

        r.Ok.Should().BeTrue();
        var llamada = registrador.Llamadas.Single().Llamada;
        llamada.Proveedor.Should().Be("kimi");
        llamada.TokensEntrada.Should().Be(400);
        llamada.TokensCacheLectura.Should().Be(600);
        llamada.TokensSalida.Should().Be(20);
        llamada.StopReason.Should().Be("stop");
        llamada.IdRespuestaProveedor.Should().Be("chatcmpl-1");
    }

    [Fact]
    public async Task OpenAiCompatible_429_RegistraRateLimit()
    {
        var registrador = new RegistradorFalso();
        var handler = Responde(() => new HttpResponseMessage(HttpStatusCode.TooManyRequests)
        {
            Content = new StringContent("""{"error":{"code":"rate_limit_exceeded","message":"slow down"}}""", Encoding.UTF8, "application/json"),
        });
        var client = new OpenAiCompatibleClient(new HttpClient(handler), NullLogger<OpenAiCompatibleClient>.Instance, registrador);

        var r = await client.SendVisionAsync(
            new OpenAiProviderOptions { ApiKey = "k", BaseUrl = "https://openai.test/v1", Model = "gpt-4o" },
            "AAAA", "image/png", "PROMPT", TestContext.Current.CancellationToken);

        r.Ok.Should().BeFalse();
        var llamada = registrador.Llamadas.Single().Llamada;
        llamada.Proveedor.Should().Be("openai");
        llamada.Resultado.Should().Be(IaResultados.RateLimit);
        llamada.ErrorCodigo.Should().Be("rate_limit_exceeded");
        llamada.TipoTareaPorDefecto.Should().Be(IaTiposTarea.Ocr);
    }

    [Fact]
    public async Task Router_CadaProveedorDeLaColaEsUnaPasadaDeLaMismaOperacion()
    {
        var settings = new AiToolProviderSettings(
            Guid.CreateVersion7(), AiToolCodes.Ocr, AiProviderCodes.Anthropic, AutoReturnToPrimary: true,
            Providers: [new AiProviderSlot(AiProviderCodes.Anthropic, true, 1), new AiProviderSlot(AiProviderCodes.OpenAi, true, 2)],
            RowVersion: 1, CreatedAt: DateTimeOffset.UtcNow, UpdatedAt: null, UpdatedBy: null);
        var repo = Substitute.For<IAiToolProviderSettingsRepository>();
        repo.GetByToolCodeAsync(AiToolCodes.Ocr, Arg.Any<CancellationToken>()).Returns(settings);
        var probe = Substitute.For<IAiApiKeyEnvironmentProbe>();
        probe.HasApiKey(Arg.Any<string>()).Returns(true);

        var vistas = new List<IaAmbitoActual?>();
        IAiVisionProvider Proveedor(string codigo, AiProviderCallResult resultado)
        {
            var p = Substitute.For<IAiVisionProvider>();
            p.ProviderCode.Returns(codigo);
            p.SendVisionAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>(),
                    Arg.Any<string?>(), Arg.Any<int?>(), Arg.Any<int?>())
                .Returns(_ =>
                {
                    vistas.Add(IaCallContext.Instancia.Actual);
                    return resultado;
                });
            return p;
        }

        var router = new AiProviderRouter(repo, probe,
            [Proveedor(AiProviderCodes.Anthropic, AiProviderCallResult.Fail(503, "caído")), Proveedor(AiProviderCodes.OpenAi, AiProviderCallResult.Success("{}"))],
            [], Substitute.For<IAdminAuditWriter>(), NullLogger<AiProviderRouter>.Instance);

        var r = await router.InvokeVisionAsync(AiToolCodes.Ocr, "AAAA", "image/png", "P", TestContext.Current.CancellationToken);

        r.Ok.Should().BeTrue();
        vistas.Select(v => v!.Ambito.Pasada).Should().Equal((short)1, (short)2);
        vistas.Select(v => v!.CorrelacionId).Distinct().Should().ContainSingle("el failover es la misma operación de negocio");
        IaCallContext.Instancia.Actual.Should().BeNull("el router cierra sus ámbitos");
    }
}
