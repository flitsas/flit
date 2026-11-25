using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using Flit.Impuestos.Api;
using Flit.Impuestos.Api.Plataforma;
using Flit.Impuestos.Tests.Api;
using Flit.Platform.Sdk.Grpc;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Flit.Impuestos.Tests.Plataforma;

/// <summary>
/// HU #14694 (M05, F5b-CF1) — el servicio de verdad registra su manifiesto al arrancar: pide el token de <c>svc-impuestos</c>
/// con scope <c>platform.manifest</c> y hace el PUT a <c>/api/v1/platform/products/impuestos/manifest</c>. Si la plataforma no
/// responde, reintenta con espera creciente sin tumbar el servicio; un 4xx definitivo no se reintenta. Identidad y el gateway son
/// dobles HTTP; no hace falta Postgres.
/// </summary>
/// <example>
/// <code>
/// dotnet test services/core-impuestos/tests/Flit.Impuestos.Tests --no-build --filter FullyQualifiedName~RegistroManifiestoTests
/// </code>
/// </example>
public sealed class RegistroManifiestoTests
{
    private const string Gateway = "http://gateway.prueba:4002";
    private const string TokenDeServicio = "token-svc-impuestos";

    [Fact]
    public async Task Envia_PUT_con_token_svc_impuestos_y_el_manifiesto_completo()
    {
        var ct = TestContext.Current.CancellationToken;
        var identidad = new Grabador(_ => TokenOk());
        var gateway = new Grabador(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") });
        await using var app = Construir(identidad, gateway, esperaInicialMs: null);

        await app.StartAsync(ct);
        var put = await gateway.Llamada(1).WaitAsync(TimeSpan.FromSeconds(15), ct);

        identidad.Llamadas.Should().ContainSingle().Which.Cuerpo.Should()
            .Contain("grant_type=client_credentials").And.Contain("client_id=svc-impuestos").And.Contain("scope=platform.manifest");
        put.Metodo.Should().Be(HttpMethod.Put);
        put.Url.Should().Be(new Uri($"{Gateway}/api/v1/platform/products/impuestos/manifest"));
        put.Autorizacion.Should().Be($"Bearer {TokenDeServicio}");

        using var json = JsonDocument.Parse(put.Cuerpo);
        var raiz = json.RootElement;
        raiz.GetProperty("version").GetString().Should().Be("1");
        var slugs = raiz.GetProperty("modules").EnumerateArray()
            .SelectMany(m => m.GetProperty("permissions").EnumerateArray().Select(p => p.GetProperty("slug").GetString()!)).ToList();
        slugs.Should().HaveCount(13).And.OnlyHaveUniqueItems().And.AllSatisfy(s => s.Should().StartWith("impuestos."));
        slugs.Should().Contain(Permisos.SoloSuperAdmin, "los permisos solo SA van en el manifiesto para un rol armado a mano");
        var roles = raiz.GetProperty("defaultRoles").EnumerateArray().ToList();
        roles.Select(r => r.GetProperty("code").GetString()).Should().BeEquivalentTo("gestor_impuestos", "admin_impuestos");
        roles.SelectMany(r => r.GetProperty("permissions").EnumerateArray().Select(p => p.GetString()))
            .Should().AllSatisfy(p => slugs.Should().Contain(p)).And.NotContain(Permisos.SoloSuperAdmin);
    }

    [Fact]
    public async Task Reintenta_con_espera_creciente_si_la_plataforma_no_responde_sin_tumbar_el_servicio()
    {
        var ct = TestContext.Current.CancellationToken;
        var identidad = new Grabador(_ => TokenOk());
        var gateway = new Grabador(n => n <= 2
            ? throw new HttpRequestException("gateway caído (doble de prueba)")
            : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") });
        await using var app = Construir(identidad, gateway, esperaInicialMs: 50);

        await app.StartAsync(ct);
        await gateway.Llamada(3).WaitAsync(TimeSpan.FromSeconds(15), ct);
        var salud = await app.GetTestClient().GetAsync("/health", ct);

        salud.StatusCode.Should().Be(HttpStatusCode.OK, "con la plataforma caída el servicio sigue atendiendo");
        app.Lifetime.ApplicationStopping.IsCancellationRequested.Should().BeFalse("un fallo del registro no tumba el host");
        gateway.Llamadas.Should().HaveCount(3, "dos fallos y el tercer intento registra; no reintenta tras el éxito");
        RegistroManifiesto.SiguienteEspera(TimeSpan.FromSeconds(5)).Should().Be(TimeSpan.FromSeconds(10));
        RegistroManifiesto.SiguienteEspera(TimeSpan.FromSeconds(200)).Should().Be(TimeSpan.FromMinutes(5), "la espera tiene techo");
    }

    [Fact]
    public async Task Un_4xx_definitivo_no_se_reintenta()
    {
        var ct = TestContext.Current.CancellationToken;
        var identidad = new Grabador(_ => TokenOk());
        var gateway = new Grabador(_ => new HttpResponseMessage(HttpStatusCode.UnprocessableEntity) { Content = new StringContent("{\"code\":\"MANIFIESTO_INVALIDO\"}") });
        await using var app = Construir(identidad, gateway, esperaInicialMs: 20);

        await app.StartAsync(ct);
        await gateway.Llamada(1).WaitAsync(TimeSpan.FromSeconds(15), ct);
        await Task.Delay(300, ct);

        gateway.Llamadas.Should().ContainSingle("un manifiesto inválido no mejora reintentando");
    }

    private static HttpResponseMessage TokenOk() => new(HttpStatusCode.OK)
    {
        Content = new StringContent(JsonSerializer.Serialize(new { access_token = TokenDeServicio, expires_in = 300 }), Encoding.UTF8, "application/json"),
    };

    private static WebApplication Construir(Grabador identidad, Grabador gateway, int? esperaInicialMs) => Program.Build([], b =>
    {
        b.WebHost.UseTestServer();
        var configuracion = ApiDePrueba.Configuracion(ApiDePrueba.BaseInalcanzable);
        configuracion[RegistroManifiesto.GatewayUrlKey] = Gateway;
        if (esperaInicialMs is { } ms)
            configuracion[RegistroManifiesto.EsperaInicialKey] = ms.ToString(System.Globalization.CultureInfo.InvariantCulture);
        b.Configuration.AddInMemoryCollection(configuracion);
        b.Services.AddHttpClient(ServiceTokenProvider.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => identidad);
        b.Services.AddHttpClient(RegistroManifiesto.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => gateway);
    });

    private sealed record Llamado(HttpMethod Metodo, Uri? Url, string? Autorizacion, string Cuerpo);

    /// <summary>Doble HTTP: graba cada petición con su cuerpo y responde según el número de llamada.</summary>
    private sealed class Grabador(Func<int, HttpResponseMessage> responder) : HttpMessageHandler
    {
        private readonly ConcurrentDictionary<int, TaskCompletionSource<Llamado>> _esperas = new();
        private readonly ConcurrentQueue<Llamado> _llamadas = new();
        private int _n;

        public IReadOnlyList<Llamado> Llamadas => [.. _llamadas];

        public Task<Llamado> Llamada(int n) => Espera(n).Task;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var cuerpo = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            var llamado = new Llamado(request.Method, request.RequestUri, request.Headers.Authorization?.ToString(), cuerpo);
            _llamadas.Enqueue(llamado);
            var n = Interlocked.Increment(ref _n);
            Espera(n).TrySetResult(llamado);
            return responder(n);
        }

        private TaskCompletionSource<Llamado> Espera(int n) =>
            _esperas.GetOrAdd(n, _ => new TaskCompletionSource<Llamado>(TaskCreationOptions.RunContinuationsAsynchronously));
    }
}
