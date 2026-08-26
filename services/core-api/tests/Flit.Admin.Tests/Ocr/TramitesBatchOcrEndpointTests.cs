using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Flit.Admin.Tests.Companies;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Flit.Admin.Tests.Ocr;

/// <summary>
/// E2E del endpoint POST /api/v1/tramites/ocr/lote (cargue masivo) por el pipeline HTTP real, con el
/// proveedor OCR en modo mock. Verifica el cableado: ruta mapeada, autenticación, binding multipart de
/// varios archivos + el campo `tipos`, expansión de .zip y forma de la respuesta que consume la pantalla
/// de revisión. La verificación con Anthropic real es un paso manual — ver docs/ocr-tramites-e2e.md.
/// </summary>
public sealed class TramitesBatchOcrEndpointTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly byte[] PdfBytes = [0x25, 0x50, 0x44, 0x46, 0x2D, 0x31, 0x2E, 0x37];
    private const string TiposMatricula = "factura,aduana,impronta,soat,rtm";

    private HttpClient AuthedClient(Guid tenantId)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestTokenFactory.CreateOtAdminToken(tenantId));
        return client;
    }

    private static HttpRequestMessage BatchRequest(
        Guid? tenantId,
        string? tipos = TiposMatricula,
        params (string Name, byte[] Content)[] files)
    {
        var content = new MultipartFormDataContent();
        if (tipos is not null)
            content.Add(new StringContent(tipos), "tipos");

        foreach (var (name, bytes) in files)
        {
            var file = new ByteArrayContent(bytes);
            file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            content.Add(file, "files", name);
        }

        var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/tramites/ocr/lote") { Content = content };
        if (tenantId.HasValue)
            req.Headers.Add("X-Tenant-Id", tenantId.Value.ToString());
        return req;
    }

    [Fact]
    public async Task Lote_Anonimo_SinTenant_NoSeProcesa()
    {
        var client = factory.CreateClient();

        var response = await client.SendAsync(
            BatchRequest(tenantId: null, files: ("doc.pdf", PdfBytes)),
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().NotBe(HttpStatusCode.OK);
        ((int)response.StatusCode).Should().BeOneOf(400, 401);
    }

    [Fact]
    public async Task Lote_Mock_DevuelveLasTresListasDeLaRevision()
    {
        var tenantId = Guid.NewGuid();

        var response = await AuthedClient(tenantId).SendAsync(
            BatchRequest(tenantId, files: ("expediente.pdf", PdfBytes)),
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<BatchBody>(
            cancellationToken: TestContext.Current.CancellationToken);

        body.Should().NotBeNull();
        // El mock reparte una página por tipo esperado y deja una sin reconocer.
        body!.Piezas.Should().HaveCount(5);
        body.Piezas.Select(p => p.Tipo).Should().BeEquivalentTo("factura", "aduana", "impronta", "soat", "rtm");
        body.Piezas.Should().OnlyContain(p => !string.IsNullOrEmpty(p.ContentBase64));
        body.NoReconocidos.Should().ContainSingle();
        body.Errores.Should().BeEmpty();
    }

    [Fact]
    public async Task Lote_RespetaLosTiposDeLaModalidad()
    {
        var tenantId = Guid.NewGuid();

        var response = await AuthedClient(tenantId).SendAsync(
            BatchRequest(tenantId, "impronta,soat,rtm", ("expediente.pdf", PdfBytes)),
            TestContext.Current.CancellationToken);

        var body = await response.Content.ReadFromJsonAsync<BatchBody>(
            cancellationToken: TestContext.Current.CancellationToken);

        // Traspaso no lleva factura ni aduana: no pueden proponerse.
        body!.Piezas.Select(p => p.Tipo).Should().BeEquivalentTo("impronta", "soat", "rtm");
    }

    [Fact]
    public async Task Lote_ExpandeUnZip()
    {
        var tenantId = Guid.NewGuid();
        var zip = Zip(("a.pdf", PdfBytes), ("b.pdf", PdfBytes));

        var response = await AuthedClient(tenantId).SendAsync(
            BatchRequest(tenantId, "soat", ("docs.zip", zip)),
            TestContext.Current.CancellationToken);

        var body = await response.Content.ReadFromJsonAsync<BatchBody>(
            cancellationToken: TestContext.Current.CancellationToken);

        body!.Piezas.Select(p => p.SourceFilename).Should().BeEquivalentTo("a.pdf", "b.pdf");
    }

    [Fact]
    public async Task Lote_ArchivoIlegible_SeReportaSinTumbarElResto()
    {
        var tenantId = Guid.NewGuid();

        var response = await AuthedClient(tenantId).SendAsync(
            BatchRequest(tenantId, "soat", ("nota.txt", [0x68, 0x6F, 0x6C, 0x61]), ("bueno.pdf", PdfBytes)),
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<BatchBody>(
            cancellationToken: TestContext.Current.CancellationToken);

        body!.Errores.Should().ContainSingle().Which.Filename.Should().Be("nota.txt");
        body.Piezas.Should().ContainSingle().Which.SourceFilename.Should().Be("bueno.pdf");
    }

    [Fact]
    public async Task Lote_SinArchivos_Devuelve400()
    {
        var tenantId = Guid.NewGuid();

        var response = await AuthedClient(tenantId).SendAsync(
            BatchRequest(tenantId),
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Lote_SinTiposValidos_Devuelve400()
    {
        var tenantId = Guid.NewGuid();

        var response = await AuthedClient(tenantId).SendAsync(
            BatchRequest(tenantId, "compraventa", ("doc.pdf", PdfBytes)),
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    private static byte[] Zip(params (string Name, byte[] Content)[] entries)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in entries)
            {
                using var s = zip.CreateEntry(name).Open();
                s.Write(content, 0, content.Length);
            }
        }
        return ms.ToArray();
    }

    private sealed record BatchBody(
        IReadOnlyList<PiezaBody> Piezas,
        IReadOnlyList<JsonElement> NoReconocidos,
        IReadOnlyList<ErrorBody> Errores);

    private sealed record PiezaBody(string Tipo, string SourceFilename, string ContentBase64);

    private sealed record ErrorBody(string Filename, string Motivo);
}
