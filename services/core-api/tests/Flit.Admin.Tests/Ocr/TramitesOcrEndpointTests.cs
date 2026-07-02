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
/// E2E del endpoint POST /api/v1/tramites/ocr/{tipo} a través del pipeline HTTP real
/// (WebApplicationFactory), con el proveedor OCR en modo mock (Ocr:Provider=mock por defecto, sin
/// llamar a Anthropic ni tocar la base de datos). Verifica el cableado extremo a extremo: ruta mapeada,
/// autenticación, binding multipart, resolución por magic bytes, forma de la respuesta y errores.
/// La verificación con el proveedor real (Anthropic) es un paso manual — ver docs/ocr-tramites-e2e.md.
/// </summary>
public sealed class TramitesOcrEndpointTests
    : IClassFixture<WebApplicationFactory<Program>>
{
    // Cabecera %PDF-1.7: el endpoint resuelve el media type por magic bytes, no por el Content-Type.
    private static readonly byte[] PdfBytes = [0x25, 0x50, 0x44, 0x46, 0x2D, 0x31, 0x2E, 0x37];

    private readonly WebApplicationFactory<Program> _factory;

    public TramitesOcrEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    private HttpClient AuthedClient(Guid tenantId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestTokenFactory.CreateOtAdminToken(tenantId));
        return client;
    }

    private static HttpRequestMessage OcrRequest(string tipo, Guid? tenantId, bool withFile = true)
    {
        var content = new MultipartFormDataContent();
        if (withFile)
        {
            var file = new ByteArrayContent(PdfBytes);
            file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
            content.Add(file, "file", "doc.pdf");
        }
        var req = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/tramites/ocr/{tipo}")
        {
            Content = content,
        };
        if (tenantId.HasValue)
            req.Headers.Add("X-Tenant-Id", tenantId.Value.ToString());
        return req;
    }

    [Fact]
    public async Task Ocr_Anonimo_SinTenant_NoSeProcesa()
    {
        // Sin token ni X-Tenant-Id: el pipeline no procesa el OCR (no llega a 200).
        // La postura de auth es la misma que la de AttachmentEndpoints (tenant vía middleware/header).
        var client = _factory.CreateClient();

        var response = await client.SendAsync(
            OcrRequest("factura", tenantId: null),
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().NotBe(HttpStatusCode.OK);
        ((int)response.StatusCode).Should().BeOneOf(400, 401);
    }

    [Fact]
    public async Task Ocr_Factura_Mock_Devuelve200ConData()
    {
        var tenantId = Guid.NewGuid();
        var client = AuthedClient(tenantId);

        var response = await client.SendAsync(
            OcrRequest("factura", tenantId),
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<OcrBody>(
            cancellationToken: TestContext.Current.CancellationToken);
        body.Should().NotBeNull();
        body!.Ok.Should().BeTrue();
        body.Tipo.Should().Be("factura");
        body.Data.GetProperty("es_factura_valida").GetBoolean().Should().BeTrue();
        body.ExtractedPdfBase64.Should().BeNull(); // mock: PDF de una página, sin recorte
    }

    [Fact]
    public async Task Ocr_Impronta_Mock_Devuelve200EsValido()
    {
        var tenantId = Guid.NewGuid();
        var client = AuthedClient(tenantId);

        var response = await client.SendAsync(
            OcrRequest("impronta", tenantId),
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<OcrBody>(
            cancellationToken: TestContext.Current.CancellationToken);
        body!.Tipo.Should().Be("impronta");
        body.Data.GetProperty("es_valido").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Ocr_TipoNoSoportado_Devuelve400()
    {
        var tenantId = Guid.NewGuid();
        var client = AuthedClient(tenantId);

        var response = await client.SendAsync(
            OcrRequest("otro", tenantId),
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Ocr_SinArchivo_Devuelve400()
    {
        var tenantId = Guid.NewGuid();
        var client = AuthedClient(tenantId);

        var response = await client.SendAsync(
            OcrRequest("factura", tenantId, withFile: false),
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    private sealed record OcrBody(bool Ok, string Tipo, JsonElement Data, string? ExtractedPdfBase64);
}
