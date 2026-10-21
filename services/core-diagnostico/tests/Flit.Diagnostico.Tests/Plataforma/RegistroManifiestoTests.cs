using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using Flit.Diagnostico.Api;
using Flit.Diagnostico.Api.Plataforma;
using Flit.Diagnostico.Tests.Api;
using Flit.Platform.Sdk.Grpc;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Flit.Diagnostico.Tests.Plataforma;

/// <summary>
/// HU #13724 (F00.1-H3), escenarios 1 y 2 — el servicio de verdad (<c>Program.Build</c>) registra su manifiesto al
/// arrancar: pide el token de <c>svc-diagnostico</c> con scope <c>platform.manifest</c> a Identidad y hace el PUT al
/// gateway. Identidad y el gateway son dobles HTTP; no hace falta Postgres.
/// </summary>
/// <example>
/// <code>
/// dotnet test services/core-diagnostico/tests/Flit.Diagnostico.Tests --no-build --filter FullyQualifiedName~RegistroManifiestoTests
/// </code>
/// </example>
public sealed class RegistroManifiestoTests
{
    private const string Gateway = "http://gateway.prueba:4002";
    private const string TokenDeServicio = "token-svc-diagnostico";

    // Escenario 1 — al arrancar, PUT del manifiesto con el token de svc-diagnostico; todos los slugs empiezan por diagnostico.
    [Fact]
    public async Task Envia_PUT_con_token_svc_diagnostico_y_cuerpo_valido()
    {
        var ct = TestContext.Current.CancellationToken;
        var identidad = new Grabador(_ => TokenOk());
        var gateway = new Grabador(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") });
        await using var app = Construir(identidad, gateway, esperaInicialMs: null);

        await app.StartAsync(ct);
        var put = await gateway.Llamada(1).WaitAsync(TimeSpan.FromSeconds(15), ct);

        var pedido = identidad.Llamadas.Should().ContainSingle().Subject;
        pedido.Cuerpo.Should().Contain("grant_type=client_credentials").And.Contain("client_id=svc-diagnostico").And.Contain("scope=platform.manifest");
        put.Metodo.Should().Be(HttpMethod.Put);
        put.Url.Should().Be(new Uri($"{Gateway}/api/v1/platform/products/diagnostico/manifest"));
        put.Autorizacion.Should().Be($"Bearer {TokenDeServicio}");

        using var json = JsonDocument.Parse(put.Cuerpo);
        var raiz = json.RootElement;
        raiz.GetProperty("version").GetString().Should().Be("1");
        var slugs = raiz.GetProperty("modules").EnumerateArray()
            .SelectMany(m => m.GetProperty("permissions").EnumerateArray().Select(p => p.GetProperty("slug").GetString()!)).ToList();
        slugs.Should().HaveCount(13).And.OnlyHaveUniqueItems().And.AllSatisfy(s => s.Should().StartWith("diagnostico."));
        raiz.GetProperty("modules").EnumerateArray().Should().AllSatisfy(m =>
        {
            m.GetProperty("code").GetString().Should().StartWith("diagnostico_");
            m.GetProperty("name").GetString().Should().NotBeNullOrWhiteSpace();
        });
        var roles = raiz.GetProperty("defaultRoles").EnumerateArray().ToList();
        roles.Select(r => r.GetProperty("code").GetString()).Should().BeEquivalentTo("usuario_diagnostico", "admin_diagnostico", "backoffice_diagnostico");
        roles.SelectMany(r => r.GetProperty("permissions").EnumerateArray().Select(p => p.GetString())).Should().AllSatisfy(p => slugs.Should().Contain(p));
    }

    // Escenario 2 — el gateway no responde: reintenta con espera creciente y el servicio sigue sano.
    [Fact]
    public async Task Reintenta_si_la_plataforma_no_responde_sin_tumbar_el_servicio()
    {
        var ct = TestContext.Current.CancellationToken;
        var identidad = new Grabador(_ => TokenOk());
        var gateway = new Grabador(n => n <= 3
            ? throw new HttpRequestException("gateway caído (doble de prueba)")
            : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") });
        await using var app = Construir(identidad, gateway, esperaInicialMs: 100);

        await app.StartAsync(ct);
        await gateway.Llamada(2).WaitAsync(TimeSpan.FromSeconds(15), ct);
        var salud = await app.GetTestClient().GetAsync("/health", ct);
        await gateway.Llamada(4).WaitAsync(TimeSpan.FromSeconds(15), ct);

        salud.StatusCode.Should().Be(HttpStatusCode.OK, "con la plataforma caída el servicio sigue atendiendo");
        app.Lifetime.ApplicationStopping.IsCancellationRequested.Should().BeFalse("un fallo del registro no tumba el host");
        gateway.Llamadas.Should().HaveCount(4, "tres fallos y el cuarto intento registra; no reintenta tras el éxito");
        var marcas = gateway.Llamadas.Select(l => l.Marca).ToList();
        var esperas = marcas.Zip(marcas.Skip(1), (a, b) => b - a).ToList();
        esperas[0].Should().BeGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(90));
        esperas[1].Should().BeGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(190)).And.BeGreaterThan(esperas[0]);
        esperas[2].Should().BeGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(390)).And.BeGreaterThan(esperas[1]);
        RegistroManifiesto.SiguienteEspera(TimeSpan.FromSeconds(5)).Should().Be(TimeSpan.FromSeconds(10));
        RegistroManifiesto.SiguienteEspera(TimeSpan.FromSeconds(200)).Should().Be(TimeSpan.FromMinutes(5), "la espera tiene techo");
    }

    private static HttpResponseMessage TokenOk() => new(HttpStatusCode.OK)
    {
        Content = new StringContent(JsonSerializer.Serialize(new { access_token = TokenDeServicio, expires_in = 300 }), Encoding.UTF8, "application/json"),
    };

    private static WebApplication Construir(Grabador identidad, Grabador gateway, int? esperaInicialMs) => Program.Build([], b =>
    {
        b.WebHost.UseTestServer();
        var configuracion = ApiDePrueba.Configuracion(ApiDePrueba.BaseInalcanzable);
        configuracion["Diagnostico:Plataforma:GatewayUrl"] = Gateway;
        if (esperaInicialMs is { } ms)
            configuracion["Diagnostico:Plataforma:EsperaInicialMs"] = ms.ToString(System.Globalization.CultureInfo.InvariantCulture);
        b.Configuration.AddInMemoryCollection(configuracion);
        b.Services.AddHttpClient(ServiceTokenProvider.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => identidad);
        b.Services.AddHttpClient(RegistroManifiesto.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => gateway);
    });

    private sealed record Llamado(HttpMethod Metodo, Uri? Url, string? Autorizacion, string Cuerpo, TimeSpan Marca);

    /// <summary>Doble HTTP: graba cada petición (con su cuerpo y el momento) y responde según el número de llamada.</summary>
    private sealed class Grabador(Func<int, HttpResponseMessage> responder) : HttpMessageHandler
    {
        private readonly Stopwatch _reloj = Stopwatch.StartNew();
        private readonly ConcurrentDictionary<int, TaskCompletionSource<Llamado>> _esperas = new();
        private readonly ConcurrentQueue<Llamado> _llamadas = new();
        private int _n;

        public IReadOnlyList<Llamado> Llamadas => [.. _llamadas];

        public Task<Llamado> Llamada(int n) => Espera(n).Task;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var cuerpo = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            var llamado = new Llamado(request.Method, request.RequestUri, request.Headers.Authorization?.ToString(), cuerpo, _reloj.Elapsed);
            _llamadas.Enqueue(llamado);
            var n = Interlocked.Increment(ref _n);
            Espera(n).TrySetResult(llamado);
            return responder(n);
        }

        private TaskCompletionSource<Llamado> Espera(int n) =>
            _esperas.GetOrAdd(n, _ => new TaskCompletionSource<Llamado>(TaskCreationOptions.RunContinuationsAsynchronously));
    }
}
