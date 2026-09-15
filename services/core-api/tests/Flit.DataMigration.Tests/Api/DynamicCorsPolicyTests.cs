extern alias gw;

using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;
using DynamicCorsOriginSource = gw::Flit.Gateway.Cors.DynamicCorsOriginSource;
using InternalApiOptions = gw::Flit.Gateway.Configuration.InternalApiOptions;

namespace Flit.DataMigration.Tests.Api;

/// <summary>
/// HU #12417 AC3/AC4 (ADR-0060 D2) — <see cref="DynamicCorsOriginSource"/> sin red real: un
/// <see cref="HttpMessageHandler"/> de prueba sustituye <c>GET /api/v1/internal/domains/active</c>.
/// Uso de ejemplo:
/// <code>
/// var allowed = await source.IsOriginAllowedAsync("https://cliente.com", fixedOrigins);
/// </code>
/// </summary>
public sealed class DynamicCorsPolicyTests
{
    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }

    private sealed class SingleClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class StaticOptionsMonitor<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue { get; } = value;

        public T Get(string? name) => CurrentValue;

        public IDisposable OnChange(Action<T, string> listener) => NullDisposable.Instance;

        private sealed class NullDisposable : IDisposable
        {
            public static readonly NullDisposable Instance = new();
            public void Dispose()
            {
            }
        }
    }

    private static (DynamicCorsOriginSource Source, MemoryCache Cache) BuildSource(
        Func<HttpRequestMessage, HttpResponseMessage> respond, string apiKey = "test-key")
    {
        var handler = new StubHandler(respond);
        var client = new HttpClient(handler) { BaseAddress = new Uri("http://core-api-test") };
        var factory = new SingleClientFactory(client);
        var options = new StaticOptionsMonitor<InternalApiOptions>(new InternalApiOptions { ApiKey = apiKey });
        var cache = new MemoryCache(new MemoryCacheOptions());
        var source = new DynamicCorsOriginSource(factory, options, cache, NullLogger<DynamicCorsOriginSource>.Instance);
        return (source, cache);
    }

    private static HttpResponseMessage HostsResponse(params string[] hosts)
    {
        var joined = string.Join(",", hosts.Select(h => $"\"{h}\""));
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent($"{{\"hosts\":[{joined}]}}", Encoding.UTF8, "application/json"),
        };
    }

    [Fact]
    public async Task OrigenDeHostActivo_EsAdmitido()
    {
        var (source, _) = BuildSource(_ => HostsResponse("cliente.movilidadandina.com"));

        var allowed = await source.IsOriginAllowedAsync("https://cliente.movilidadandina.com", [], TestContext.Current.CancellationToken);

        allowed.Should().BeTrue();
    }

    [Fact]
    public async Task OrigenNoRegistrado_EsRechazado()
    {
        var (source, _) = BuildSource(_ => HostsResponse("otra-red.com"));

        var allowed = await source.IsOriginAllowedAsync("https://no-registrado.com", [], TestContext.Current.CancellationToken);

        allowed.Should().BeFalse();
    }

    [Fact]
    public async Task ListaFija_SeAdmiteSinConsultarLaApiInterna()
    {
        var called = false;
        var (source, _) = BuildSource(_ => { called = true; return HostsResponse(); });

        var allowed = await source.IsOriginAllowedAsync("https://app.flit.co", ["https://app.flit.co"], TestContext.Current.CancellationToken);

        allowed.Should().BeTrue();
        called.Should().BeFalse("la lista fija se evalúa ANTES de consultar el dominio dinámico");
    }

    [Fact]
    public async Task HostRetirado_TrasExpirarElCache_QuedaDenegado()
    {
        // AC3 — "un dominio recién activado o retirado se refleja a más tardar 60 segundos después".
        var attempt = 0;
        var (source, cache) = BuildSource(_ =>
        {
            attempt++;
            return attempt == 1 ? HostsResponse("cliente.movilidadandina.com") : HostsResponse();
        });

        (await source.IsOriginAllowedAsync("https://cliente.movilidadandina.com", [], TestContext.Current.CancellationToken)).Should().BeTrue();

        // Simula la expiración del caché de 60 s (Compact(1.0) evicta TODAS las entradas vivas).
        cache.Compact(1.0);

        (await source.IsOriginAllowedAsync("https://cliente.movilidadandina.com", [], TestContext.Current.CancellationToken)).Should().BeFalse(
            "tras refrescar el caché, el host retirado ya no figura entre los activos");
    }

    [Fact]
    public async Task FalloTransitorioTrasUnExitoPrevio_RespaldaEnLaUltimaListaBuena()
    {
        // AC4 — CORS nunca queda vacío por un fallo transitorio de la API interna.
        var attempt = 0;
        var (source, cache) = BuildSource(_ =>
        {
            attempt++;
            return attempt == 1
                ? HostsResponse("cliente.movilidadandina.com")
                : new HttpResponseMessage(HttpStatusCode.InternalServerError);
        });

        (await source.IsOriginAllowedAsync("https://cliente.movilidadandina.com", [], TestContext.Current.CancellationToken)).Should().BeTrue();
        cache.Compact(1.0);

        (await source.IsOriginAllowedAsync("https://cliente.movilidadandina.com", [], TestContext.Current.CancellationToken)).Should().BeTrue(
            "un fallo transitorio de GET /internal/domains/active no debe vaciar CORS");
    }

    [Fact]
    public async Task FalloSinListaBuenaPrevia_SoloAdmiteLaListaFija()
    {
        var (source, _) = BuildSource(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));

        var allowed = await source.IsOriginAllowedAsync("https://cliente.movilidadandina.com", ["https://app.flit.co"], TestContext.Current.CancellationToken);

        allowed.Should().BeFalse("sin lista buena previa y con la API caída, no hay origen dinámico que admitir");
    }

    [Fact]
    public async Task SinClaveConfigurada_NuncaLlamaALaApiInterna()
    {
        var called = false;
        var (source, _) = BuildSource(_ => { called = true; return HostsResponse("cliente.movilidadandina.com"); }, apiKey: "");

        var allowed = await source.IsOriginAllowedAsync("https://cliente.movilidadandina.com", [], TestContext.Current.CancellationToken);

        allowed.Should().BeFalse();
        called.Should().BeFalse("Internal:ApiKey vacío es fail-closed: nunca se llama a la API interna");
    }
}
