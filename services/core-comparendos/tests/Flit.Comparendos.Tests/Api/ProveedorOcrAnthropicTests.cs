using System.Net;
using System.Text;
using System.Text.Json;
using Flit.Comparendos.Api.Modulos.Ocr;
using Flit.Comparendos.Domain.Modelo;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Flit.Comparendos.Tests.Api;

/// <summary>
/// Cómo se interpreta la respuesta de Anthropic. Haiku 5.5 piensa antes de responder y ese pensamiento cuenta contra
/// max_tokens: una respuesta cortada o rechazada no es «ilegible» sin más, el mensaje dice la causa.
/// </summary>
public sealed class ProveedorOcrAnthropicTests
{
    private sealed class Respuesta(string cuerpo) : HttpMessageHandler
    {
        public string? Pedido { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Pedido = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(cuerpo, Encoding.UTF8, "application/json") };
        }
    }

    private static string Cuerpo(string stopReason, string texto) => JsonSerializer.Serialize(new
    {
        stop_reason = stopReason,
        content = new object[] { new { type = "thinking", thinking = "" }, new { type = "text", text = texto } },
    });

    private static async Task<(ResultadoOcr Resultado, string? Pedido)> ExtraerAsync(string cuerpo)
    {
        var handler = new Respuesta(cuerpo);
        var proveedor = new ProveedorOcrAnthropic(new HttpClient(handler), Options.Create(new OpcionesAnthropic { ApiKey = "k" }));
        var r = await proveedor.ExtraerAsync([1, 2, 3], "application/pdf", "prompt", "claude-haiku-5-5", CancellationToken.None);
        return (r, handler.Pedido);
    }

    [Fact]
    public async Task RespuestaCortada_DiceQueSeAcaboElLimite()
    {
        var (r, pedido) = await ExtraerAsync(Cuerpo("max_tokens", "{\"numero_comparendo\": {\"valor\": \"D0500"));
        (r.TipoFallo, r.Mensaje).Should().Be((TipoFalloOcr.Ilegible, "La respuesta del modelo quedó cortada (se acabó el límite de tokens)."));
        JsonDocument.Parse(pedido!).RootElement.GetProperty("max_tokens").GetInt32().Should().Be(16000);
    }

    [Fact]
    public async Task Rechazo_DiceQueNoQuisoLeerlo()
    {
        var (r, _) = await ExtraerAsync(Cuerpo("refusal", ""));
        r.Mensaje.Should().Be("El modelo no quiso leer el documento.");
    }

    [Fact]
    public async Task RespuestaCompleta_IgnoraElBloqueDePensamiento()
    {
        var (r, _) = await ExtraerAsync(Cuerpo("end_turn", "```json\n{\"numero_comparendo\": {\"valor\": \"D05001000000054220962\", \"confianza\": 0.97}}\n```"));
        r.Exitoso.Should().BeTrue();
        r.Campos[CamposOcr.Numero].Valor.Should().Be("D05001000000054220962");
    }
}
