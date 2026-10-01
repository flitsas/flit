using System.Net;
using System.Text;
using Flit.Infrastructure.Storage;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Flit.Infrastructure.Tests.Storage;

/// <summary>
/// HU #13232 (Epic #13217) — lector de logos de core-identity: lee del file-manager con la misma descarga que el
/// almacenamiento de adjuntos de core-api y se niega a guardar (los logos se suben desde core-api).
/// </summary>
public sealed class FileManagerBrandLogoReaderTests
{
    private const string Base = "https://fm.test/pdn/";

    [Fact]
    public async Task OpenReadAsync_ResuelvePresignedYDescargaElLogo()
    {
        var ct = TestContext.Current.CancellationToken;
        var reader = Reader((req, _) =>
            req.RequestUri!.AbsolutePath == "/pdn/api/v1/files/logo_1/presigned-url"
                ? new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""{"presignedUrl":{"url":"https://s3.test/logo"}}""", Encoding.UTF8, "application/json"),
                }
                : req.RequestUri!.Host == "s3.test"
                    ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Encoding.UTF8.GetBytes("PNG")) }
                    : new HttpResponseMessage(HttpStatusCode.InternalServerError));

        await using var stream = await reader.OpenReadAsync("logo_1", ct);

        using var ms = new MemoryStream();
        await stream!.CopyToAsync(ms, ct);
        Encoding.UTF8.GetString(ms.ToArray()).Should().Be("PNG");
    }

    [Fact]
    public async Task OpenReadAsync_SinRutaOInexistente_DevuelveNull()
    {
        var ct = TestContext.Current.CancellationToken;
        var reader = Reader((_, _) => new HttpResponseMessage(HttpStatusCode.NotFound));

        (await reader.OpenReadAsync("", ct)).Should().BeNull();
        (await reader.OpenReadAsync("no-existe", ct)).Should().BeNull();
    }

    [Fact]
    public async Task SaveAsync_NoEstaPermitidoEnCoreIdentity()
    {
        var reader = Reader((_, _) => new HttpResponseMessage(HttpStatusCode.OK));

        var save = () => reader.SaveAsync(Guid.NewGuid(), "logo.png", new MemoryStream(), TestContext.Current.CancellationToken);

        await save.Should().ThrowAsync<NotSupportedException>();
    }

    private static FileManagerBrandLogoReader Reader(Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> responder) =>
        new(
            new HttpClient(new Handler(responder)) { BaseAddress = new Uri(Base) },
            Options.Create(new FileManagerOptions { BaseUrl = Base, FilesPath = "api/v1/files" }));

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(responder(request, cancellationToken));
    }
}
