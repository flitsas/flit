using System.Net;
using System.Text;
using Flit.Infrastructure.Consultations;
using Flit.Infrastructure.Consultations.Avaluos;
using Flit.Tramites.Application.UseCases.Avaluos;
using Microsoft.Extensions.Options;
using Xunit;

namespace Flit.Infrastructure.Tests.Consultations;

/// <summary>
/// El token real de Fasecolda incluye <c>/</c> y <c>=</c>. <c>AuthenticationHeaderValue</c> los rechaza
/// y la consulta por código no sale. El header debe ir en crudo, como el Bearer de Postman.
/// </summary>
public sealed class FasecoldaAvaluoProviderTests
{
    [Fact]
    public async Task Real_envia_el_bearer_aunque_el_token_tenga_barra_y_igual()
    {
        const string token = "abc/def=";
        string? authorization = null;
        string? userAgentVin = null;
        var handler = new FasecoldaFalsa(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.Contains("busquedaVin", StringComparison.Ordinal))
            {
                userAgentVin = request.Headers.TryGetValues("User-Agent", out var agents) ? string.Join(" ", agents) : null;
                return Json("""{"message":"Exito","codigos":["09201051"]}""");
            }
            if (path.EndsWith("/token", StringComparison.Ordinal))
                return Json($$"""{"access_token":"{{token}}","token_type":"bearer","expires_in":3600}""");
            if (path.Contains("consultabycodigo", StringComparison.Ordinal))
            {
                authorization = request.Headers.TryGetValues("Authorization", out var values) ? values.Single() : null;
                return Json("""[{"valorModelo":[{"modelo":"2020","valor":52000}]}]""");
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var provider = new FasecoldaAvaluoProvider(
            new Fabrica(handler),
            Options.Create(new FasecoldaOptions
            {
                ByVinBaseUrl = "https://vin.test",
                ApiBaseUrl = "https://api.test/apifasecolda",
                Username = "u",
                Password = "p",
            }),
            Options.Create(new ConsultationProviderModeOptions { FasecoldaMode = "real" }),
            new FasecoldaTokenCache(),
            new SinValoresMock());

        var result = await provider.GetAvaluoAsync(
            new AvaluoContext(Guid.Empty, Guid.Empty, new Dictionary<string, string?>
            {
                ["vin"] = "9BWZZZ377VT004251",
                ["vehicle_year"] = "2020",
            }),
            CancellationToken.None);

        Assert.Equal("ok", result.Status);
        Assert.Equal(52_000_000, result.Value);
        Assert.Equal("Bearer " + token, authorization);
        Assert.Equal("' '", userAgentVin);
    }

    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private sealed class SinValoresMock : IAvaluoMockValueSource
    {
        public Task<long?> GetValueAsync(string matchKey, string source, CancellationToken ct) =>
            throw new InvalidOperationException("El modo real no lee valores mock.");
    }

    private sealed class Fabrica(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class FasecoldaFalsa(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(responder(request));
    }
}
