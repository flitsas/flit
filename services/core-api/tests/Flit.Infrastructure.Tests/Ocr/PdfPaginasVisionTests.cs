using System.Net;
using System.Text;
using System.Text.Json;
using Flit.Infrastructure.Ai.Providers;
using Flit.Infrastructure.Ocr;
using Flit.Platform.Sdk.IaConsumo;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;
using Xunit;

namespace Flit.Infrastructure.Tests.Ocr;

/// <summary>
/// Los proveedores OpenAI-compatible (OpenAI, Kimi) no aceptan PDF en <c>image_url</c>: el cliente
/// convierte cada página en JPEG antes de llamar. Anthropic no cambia (recibe el PDF como documento).
/// </summary>
public sealed class PdfPaginasVisionTests
{
    static PdfPaginasVisionTests() => QuestPDF.Settings.License = LicenseType.Community;

    private static byte[] Pdf(int paginas) =>
        Document.Create(c =>
        {
            for (var i = 1; i <= paginas; i++)
            {
                var n = i;
                c.Page(p => p.Content().Text($"Página {n}"));
            }
        }).GeneratePdf();

    private static bool EsJpeg(byte[] b) => b.Length > 3 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF;

    [Fact]
    public void AJpeg_DevuelveUnJpegPorPagina()
    {
        var paginas = PdfPaginasVision.AJpeg(Pdf(3));

        paginas.Should().NotBeNull().And.HaveCount(3);
        paginas!.Should().OnlyContain(p => EsJpeg(p));
    }

    [Fact]
    public void AJpeg_RespetaElTopeDePaginas()
    {
        PdfPaginasVision.AJpeg(Pdf(5), maxPaginas: 2).Should().HaveCount(2);
    }

    [Theory]
    [InlineData(new byte[0])]
    [InlineData(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 })]
    public void AJpeg_PdfIlegible_DevuelveNullSinLanzar(byte[] basura)
    {
        PdfPaginasVision.AJpeg(basura).Should().BeNull();
    }

    /// <summary>
    /// La sonda de orientación endereza reescribiendo <c>/Rotate</c> y vuelve a preguntar. Con OpenAI o Kimi
    /// la página viaja como imagen: si el rasterizado ignorara <c>/Rotate</c>, el modelo vería siempre la
    /// misma imagen y nunca daría la página por derecha.
    /// </summary>
    [Fact]
    public void AJpeg_RespetaElGiroQueDeclaraElPdf()
    {
        var vertical = Pdf(1);
        var girado = new PdfSharpPageExtractor(NullLogger<PdfSharpPageExtractor>.Instance).Rotate(vertical, 1);
        girado.Should().NotBeNull();

        var (anchoV, altoV) = Tamano(PdfPaginasVision.AJpeg(vertical)![0]);
        var (anchoG, altoG) = Tamano(PdfPaginasVision.AJpeg(girado!)![0]);

        altoV.Should().BeGreaterThan(anchoV, "la página A4 sale vertical");
        anchoG.Should().BeGreaterThan(altoG, "un cuarto de vuelta la deja horizontal");
        (anchoG, altoG).Should().Be((altoV, anchoV));
    }

    private static (int Ancho, int Alto) Tamano(byte[] jpeg)
    {
        using var codec = SkiaSharp.SKCodec.Create(new MemoryStream(jpeg));
        return (codec.Info.Width, codec.Info.Height);
    }

    private sealed class Captura : HttpMessageHandler
    {
        public string? Cuerpo { get; private set; }
        public int Llamadas { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Llamadas++;
            Cuerpo = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"id":"c1","model":"kimi-k3","choices":[{"message":{"content":"{\"placa\":\"ABC123\"}"},"finish_reason":"stop"}],"usage":{"prompt_tokens":10,"completion_tokens":5}}""",
                    Encoding.UTF8, "application/json"),
            };
        }
    }

    private sealed class Registrador : IIaConsumoRegistrador
    {
        public List<IaLlamada> Llamadas { get; } = [];
        public IIaCallContext Contexto => IaCallContext.Instancia;
        public bool Activo => true;
        public bool DetalleActivo => false;
        public void Registrar(IaLlamada llamada) => Llamadas.Add(llamada);
    }

    private static List<string> UrlsDeImagen(string cuerpo)
    {
        using var doc = JsonDocument.Parse(cuerpo);
        return doc.RootElement.GetProperty("messages")[0].GetProperty("content").EnumerateArray()
            .Where(p => p.GetProperty("type").GetString() == "image_url")
            .Select(p => p.GetProperty("image_url").GetProperty("url").GetString()!)
            .ToList();
    }

    private static KimiProviderOptions Kimi() =>
        new() { ApiKey = "k", BaseUrl = "https://kimi.test/v1", Model = "kimi-k3" };

    [Fact]
    public async Task SendVision_ConPdf_MandaUnaImagenJpegPorPaginaYNuncaElPdf()
    {
        var http = new Captura();
        var registrador = new Registrador();
        var client = new OpenAiCompatibleClient(new HttpClient(http), NullLogger<OpenAiCompatibleClient>.Instance, registrador);
        var pdf = Pdf(2);

        var r = await client.SendVisionAsync(Kimi(), Convert.ToBase64String(pdf), "application/pdf", "PROMPT",
            TestContext.Current.CancellationToken);

        r.Ok.Should().BeTrue();
        var urls = UrlsDeImagen(http.Cuerpo!);
        urls.Should().HaveCount(2).And.OnlyContain(u => u.StartsWith("data:image/jpeg;base64,", StringComparison.Ordinal));
        http.Cuerpo.Should().NotContain("application/pdf");
        var llamada = registrador.Llamadas.Single();
        llamada.ArchivoMime.Should().Be("application/pdf", "el registro describe el archivo que entró, no la conversión");
        llamada.ArchivoBytes.Should().Be(pdf.Length);
    }

    [Fact]
    public async Task SendVision_ConImagen_LaMandaTalCual()
    {
        var http = new Captura();
        var client = new OpenAiCompatibleClient(new HttpClient(http), NullLogger<OpenAiCompatibleClient>.Instance);

        await client.SendVisionAsync(Kimi(), "AAAA", "image/png", "PROMPT", TestContext.Current.CancellationToken);

        UrlsDeImagen(http.Cuerpo!).Should().Equal("data:image/png;base64,AAAA");
    }

    [Fact]
    public async Task SendVision_PdfIlegible_NoLlamaAlProveedorYRegistraRechazo()
    {
        var http = new Captura();
        var registrador = new Registrador();
        var client = new OpenAiCompatibleClient(new HttpClient(http), NullLogger<OpenAiCompatibleClient>.Instance, registrador);

        var r = await client.SendVisionAsync(Kimi(), Convert.ToBase64String(new byte[300]), "application/pdf", "PROMPT",
            TestContext.Current.CancellationToken);

        r.Ok.Should().BeFalse();
        r.Status.Should().Be(503, "el router pasa al siguiente proveedor de la cola");
        http.Llamadas.Should().Be(0);
        var llamada = registrador.Llamadas.Single();
        llamada.Resultado.Should().Be(IaResultados.Rechazado);
        llamada.ErrorCodigo.Should().Be("pdf_no_rasterizable");
        llamada.Proveedor.Should().Be("kimi");
    }
}
