using System.Net;
using System.Text;
using Flit.Infrastructure.Storage;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Flit.Infrastructure.Tests.Storage;

/// <summary>
/// Contrato del cliente del file-manager (BackCrudFileManager · api/v1/files):
/// SaveAsync = POST /files (crea + presigned) y POST multipart a S3; OpenReadAsync = GET
/// /files/{id}/presigned-url y GET a S3; Delete = no-op (el file-manager no expone borrado).
/// </summary>
public sealed class FileManagerAttachmentStorageTests
{
    private const string Base = "https://fm.test/pdn/";

    private static FileManagerAttachmentStorage Storage(MockHttpMessageHandler handler) =>
        new(
            new HttpClient(handler) { BaseAddress = new Uri(Base) },
            Options.Create(new FileManagerOptions
            {
                BaseUrl = Base,
                FilesPath = "api/v1/files",
                Category = "tramites",
            }));

    [Fact]
    public async Task SaveAsync_CreaRegistroYSubeAS3_DevuelveIdComoStoragePathConShaYSize()
    {
        var ct = TestContext.Current.CancellationToken;
        var handler = new MockHttpMessageHandler((req, _) =>
        {
            if (req.Method == HttpMethod.Post && req.RequestUri!.AbsolutePath == "/pdn/api/v1/files")
                return Json(HttpStatusCode.Created,
                    """{"id":"file_abc","presignedUrl":{"url":"https://s3.test/upload","fields":{"key":"tramites/k/obj","policy":"pol"}}}""");
            if (req.Method == HttpMethod.Post && req.RequestUri!.Host == "s3.test")
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            return new HttpResponseMessage(HttpStatusCode.InternalServerError);
        });

        await using var content = new MemoryStream(Encoding.UTF8.GetBytes("hola-mundo"));
        var result = await Storage(handler).SaveAsync(
            Guid.Parse("11111111-1111-1111-1111-111111111111"), "factura", "f.pdf", content, ct);

        // StoragePath = id del file-manager; sha256 hex(64) y size del contenido.
        result.StoragePath.Should().Be("file_abc");
        result.SizeBytes.Should().Be(10);
        result.Sha256.Should().HaveLength(64).And.MatchRegex("^[0-9a-f]{64}$");

        // El create llevó category/filename/tags/metadata del trámite.
        var createBody = handler.Requests.First(r => r.Uri.AbsolutePath.EndsWith("/files")).Body;
        createBody.Should().Contain("\"category\":\"tramites\"");
        createBody.Should().Contain("\"filename\":\"f.pdf\"");
        createBody.Should().Contain("factura");
        createBody.Should().Contain("11111111-1111-1111-1111-111111111111");

        // La subida a S3 fue multipart e incluyó los fields firmados + el file.
        var upload = handler.Requests.First(r => r.Uri.Host == "s3.test");
        upload.ContentType.Should().StartWith("multipart/form-data");
        upload.Body.Should().Contain("tramites/k/obj").And.Contain("pol").And.Contain("hola-mundo");
    }

    [Fact]
    public async Task CreatePresignedUploadAsync_CreaRegistroYDevuelvePresigned_SinSubirAS3()
    {
        var ct = TestContext.Current.CancellationToken;
        var handler = new MockHttpMessageHandler((req, _) =>
        {
            if (req.Method == HttpMethod.Post && req.RequestUri!.AbsolutePath == "/pdn/api/v1/files")
                return Json(HttpStatusCode.Created,
                    """{"id":"file_xyz","presignedUrl":{"url":"https://s3.test/upload","fields":{"key":"tramites/k/obj","policy":"pol"}}}""");
            // Cualquier llamada a S3 sería un error: el presign NO sube el binario.
            return new HttpResponseMessage(HttpStatusCode.InternalServerError);
        });

        var result = await Storage(handler).CreatePresignedUploadAsync(
            Guid.Parse("22222222-2222-2222-2222-222222222222"), "factura", "f.pdf", ct);

        // StoragePath = id del file-manager; url + fields = POST policy para el cliente.
        result.StoragePath.Should().Be("file_xyz");
        result.Url.Should().Be("https://s3.test/upload");
        result.Fields.Should().Contain("key", "tramites/k/obj").And.Contain("policy", "pol");

        // Solo se llamó al file-manager (create); NUNCA a S3 (el binario lo sube el navegador).
        handler.Requests.Should().ContainSingle();
        var createBody = handler.Requests.Single().Body;
        createBody.Should().Contain("\"category\":\"tramites\"")
            .And.Contain("\"filename\":\"f.pdf\"")
            .And.Contain("factura")
            .And.Contain("22222222-2222-2222-2222-222222222222");
        // El sha256 NO va en metadata: lo calcula el cliente al registrar.
        createBody.Should().NotContain("sha256");
    }

    [Fact]
    public async Task OpenReadAsync_ResuelvePresignedYDescargaBytes()
    {
        var ct = TestContext.Current.CancellationToken;
        var handler = new MockHttpMessageHandler((req, _) =>
        {
            if (req.Method == HttpMethod.Get && req.RequestUri!.AbsolutePath == "/pdn/api/v1/files/file_abc/presigned-url")
                return Json(HttpStatusCode.OK, """{"presignedUrl":{"url":"https://s3.test/download"}}""");
            if (req.Method == HttpMethod.Get && req.RequestUri!.Host == "s3.test")
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Encoding.UTF8.GetBytes("PDFBYTES")) };
            return new HttpResponseMessage(HttpStatusCode.InternalServerError);
        });

        await using var stream = await Storage(handler).OpenReadAsync("file_abc", ct);

        stream.Should().NotBeNull();
        using var ms = new MemoryStream();
        await stream!.CopyToAsync(ms, ct);
        Encoding.UTF8.GetString(ms.ToArray()).Should().Be("PDFBYTES");
    }

    [Fact]
    public async Task OpenReadAsync_PresignedNotFound_DevuelveNull()
    {
        var ct = TestContext.Current.CancellationToken;
        var handler = new MockHttpMessageHandler((_, _) => new HttpResponseMessage(HttpStatusCode.NotFound));

        var stream = await Storage(handler).OpenReadAsync("missing", ct);

        stream.Should().BeNull();
    }

    [Fact]
    public void Delete_EsNoOp_NoLanzaNiLlamaAlServicio()
    {
        var handler = new MockHttpMessageHandler((_, _) => new HttpResponseMessage(HttpStatusCode.InternalServerError));

        var act = () => Storage(handler).Delete("file_abc");

        act.Should().NotThrow();
        handler.Requests.Should().BeEmpty();
    }

    private static HttpResponseMessage Json(HttpStatusCode code, string body) =>
        new(code) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private sealed record CapturedRequest(HttpMethod Method, Uri Uri, string Body, string? ContentType);

    private sealed class MockHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> responder)
        : HttpMessageHandler
    {
        public List<CapturedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(new CapturedRequest(
                request.Method, request.RequestUri!, body, request.Content?.Headers.ContentType?.ToString()));
            return responder(request, cancellationToken);
        }
    }
}
