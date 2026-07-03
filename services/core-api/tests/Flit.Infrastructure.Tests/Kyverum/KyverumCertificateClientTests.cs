using System.Net;
using System.Net.Http.Headers;
using Flit.Infrastructure.Kyverum;
using Flit.Tramites.Application.Identity;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Flit.Infrastructure.Tests.Kyverum;

public sealed class KyverumCertificateClientTests
{
    private static KyverumCertificateClient Client(MockHttpMessageHandler handler) =>
        new(
            new HttpClient(handler) { BaseAddress = new Uri("https://verify.kyverum.test") },
            Options.Create(new KyverumOptions { BaseUrl = "https://verify.kyverum.test", ApiKey = "secret-key" }),
            NullLogger<KyverumCertificateClient>.Instance);

    [Fact]
    public async Task Download_Success_ReturnsPdfWithApiKeyAndCorrectPath()
    {
        var ct = TestContext.Current.CancellationToken;
        var pdf = new byte[] { 0x25, 0x50, 0x44, 0x46 }; // %PDF
        var handler = new MockHttpMessageHandler((_, _) => Pdf(pdf, "Certificacion_Identidad_ext.pdf"));

        var result = await Client(handler).DownloadCertificateAsync("kyv_1", ct);

        result.Should().NotBeNull();
        result!.Content.Should().Equal(pdf);
        result.ContentType.Should().Be("application/pdf");
        // Nombre del Content-Disposition de Kyverum.
        result.FileName.Should().Be("Certificacion_Identidad_ext.pdf");
        // GET al endpoint público correcto, con el Bearer API key.
        handler.LastRequest!.Method.Should().Be(HttpMethod.Get);
        handler.LastRequest!.RequestUri!.AbsolutePath.Should().Be("/v1/validations/kyv_1/certificado");
        handler.LastRequest!.Headers.Authorization!.Parameter.Should().Be("secret-key");
    }

    [Fact]
    public async Task Download_NotFound_ReturnsNull()
    {
        var ct = TestContext.Current.CancellationToken;
        var handler = new MockHttpMessageHandler((_, _) => new HttpResponseMessage(HttpStatusCode.NotFound));

        var result = await Client(handler).DownloadCertificateAsync("kyv_x", ct);

        result.Should().BeNull();
    }

    [Fact]
    public async Task Download_4xx_ThrowsDefinitiveWithoutApiKey()
    {
        var ct = TestContext.Current.CancellationToken;
        var handler = new MockHttpMessageHandler((_, _) => new HttpResponseMessage(HttpStatusCode.BadRequest));

        var act = async () => await Client(handler).DownloadCertificateAsync("kyv_1", ct);

        var ex = await act.Should().ThrowAsync<KyverumCertificateException>();
        ex.Which.Transient.Should().BeFalse();
        ex.Which.Message.Should().NotContain("secret-key");
    }

    [Fact]
    public async Task Download_5xx_ThrowsTransient()
    {
        var ct = TestContext.Current.CancellationToken;
        var handler = new MockHttpMessageHandler((_, _) => new HttpResponseMessage(HttpStatusCode.InternalServerError));

        var act = async () => await Client(handler).DownloadCertificateAsync("kyv_1", ct);

        (await act.Should().ThrowAsync<KyverumCertificateException>()).Which.Transient.Should().BeTrue();
    }

    [Fact]
    public async Task Download_Timeout_ThrowsTransient()
    {
        var ct = TestContext.Current.CancellationToken;
        var handler = new MockHttpMessageHandler((_, _) => throw new TaskCanceledException("timeout"));

        var act = async () => await Client(handler).DownloadCertificateAsync("kyv_1", ct);

        (await act.Should().ThrowAsync<KyverumCertificateException>()).Which.Transient.Should().BeTrue();
    }

    [Fact]
    public async Task Download_EmptyBody_ThrowsDefinitive()
    {
        var ct = TestContext.Current.CancellationToken;
        var handler = new MockHttpMessageHandler((_, _) => Pdf([], null));

        var act = async () => await Client(handler).DownloadCertificateAsync("kyv_1", ct);

        (await act.Should().ThrowAsync<KyverumCertificateException>()).Which.Transient.Should().BeFalse();
    }

    [Fact]
    public async Task Download_BlankId_ThrowsDefinitive()
    {
        var ct = TestContext.Current.CancellationToken;
        var handler = new MockHttpMessageHandler((_, _) => Pdf([1], null));

        var act = async () => await Client(handler).DownloadCertificateAsync("  ", ct);

        (await act.Should().ThrowAsync<KyverumCertificateException>()).Which.Transient.Should().BeFalse();
    }

    [Fact]
    public async Task Download_NoContentDisposition_FallsBackToDefaultName()
    {
        var ct = TestContext.Current.CancellationToken;
        var handler = new MockHttpMessageHandler((_, _) => Pdf([1, 2], null));

        var result = await Client(handler).DownloadCertificateAsync("kyv_9", ct);

        result!.FileName.Should().Be("certificado_identidad_kyv_9.pdf");
    }

    private static HttpResponseMessage Pdf(byte[] bytes, string? fileName)
    {
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        if (fileName is not null)
            content.Headers.ContentDisposition = new ContentDispositionHeaderValue("inline") { FileName = $"\"{fileName}\"" };
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
    }

    private sealed class MockHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> responder)
        : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(responder(request, cancellationToken));
        }
    }
}
