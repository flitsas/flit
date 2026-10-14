using System.Diagnostics;
using System.Net;
using Flit.Api.Middleware;
using Flit.Api.Telemetry;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Trace;
using Xunit;

namespace Flit.Identity.Tests.Telemetry;

/// <summary>
/// HU #13332 (Epic #13316): identificador de correlación y telemetría OTLP de los servicios (Flit.Suite.AspNetCore).
/// </summary>
public sealed class FlitTelemetryTests
{
    private static readonly Action<ILogger, Exception?> Procesando =
        LoggerMessage.Define(LogLevel.Information, new EventId(1, "Procesando"), "procesando");

    [Theory]
    [InlineData("0192f4c1-7e3a-7c1b-9f00-123456789abc", "0192f4c1-7e3a-7c1b-9f00-123456789abc")]
    [InlineData("  abc-123  ", "abc-123")]
    [InlineData("req_1.a:b", "req_1.a:b")]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("con espacio", null)]
    [InlineData("salto\nde-linea", null)]
    [InlineData("<script>", null)]
    public void UnIdEntranteSoloSeAceptaSiParecUnId(string? entrante, string? esperado) =>
        CorrelationIdMiddleware.Normalize(entrante).Should().Be(esperado);

    [Fact]
    public void UnIdEntranteDemasiadoLargoSeDescarta() =>
        CorrelationIdMiddleware.Normalize(new string('a', 65)).Should().BeNull();

    [Fact]
    public async Task ConservaElIdQueTraeLaPeticionYLoDevuelve()
    {
        using var host = await StartAsync(configure: null);

        var request = new HttpRequestMessage(HttpMethod.Get, "/eco");
        request.Headers.Add(CorrelationIdMiddleware.HeaderName, "id-del-gateway");
        var response = await host.GetTestClient().SendAsync(request, TestContext.Current.CancellationToken);

        response.Headers.GetValues(CorrelationIdMiddleware.HeaderName).Should().ContainSingle().Which.Should().Be("id-del-gateway");
        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).Should().Be("id-del-gateway");
    }

    [Fact]
    public async Task SinIdEntranteCreaUnoYLoDevuelve()
    {
        using var host = await StartAsync(configure: null);

        var response = await host.GetTestClient().GetAsync("/eco", TestContext.Current.CancellationToken);

        var id = response.Headers.GetValues(CorrelationIdMiddleware.HeaderName).Should().ContainSingle().Subject;
        Guid.TryParse(id, out _).Should().BeTrue();
        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).Should().Be(id);
    }

    [Fact]
    public async Task CadaLogDeLaPeticionLlevaElId()
    {
        var logs = new ScopeCapture();
        using var host = await StartAsync(configure: b => b.Logging.AddProvider(logs));

        var request = new HttpRequestMessage(HttpMethod.Get, "/eco");
        request.Headers.Add(CorrelationIdMiddleware.HeaderName, "id-de-log");
        await host.GetTestClient().SendAsync(request, TestContext.Current.CancellationToken);

        logs.ScopesOf("eco").Should().ContainKey(CorrelationIdMiddleware.LogScopeKey)
            .WhoseValue.Should().Be("id-de-log");
    }

    [Fact]
    public async Task LaTrazaQuedaMarcadaConElId()
    {
        using var listener = new ActivityListener
        {
            ShouldListenTo = s => s.Name == "Microsoft.AspNetCore",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
        };
        ActivitySource.AddActivityListener(listener);
        using var host = await StartAsync(configure: null);

        var request = new HttpRequestMessage(HttpMethod.Get, "/traza");
        request.Headers.Add(CorrelationIdMiddleware.HeaderName, "id-de-traza");
        var response = await host.GetTestClient().SendAsync(request, TestContext.Current.CancellationToken);

        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).Should().Be("id-de-traza");
    }

    [Fact]
    public void SinColectorConfiguradoNoSeRegistraTelemetria()
    {
        var builder = WebApplication.CreateBuilder();
        builder.AddFlitTelemetry("prueba");

        using var app = builder.Build();
        app.Services.GetService<TracerProvider>().Should().BeNull("sin OTEL_EXPORTER_OTLP_ENDPOINT el servicio queda como antes");
    }

    [Fact]
    public void ConColectorConfiguradoSeRegistraTelemetria()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Configuration[FlitTelemetryExtensions.EndpointKey] = "http://127.0.0.1:4317";
        builder.AddFlitTelemetry("prueba");

        using var app = builder.Build();
        app.Services.GetService<TracerProvider>().Should().NotBeNull();
    }

    [Fact]
    public async Task ConElColectorCaidoLasPeticionesRespondenIgual()
    {
        // Puerto 1 en loopback: no hay nadie escuchando, el exportador no puede entregar.
        using var host = await StartAsync(configure: b => b.Configuration[FlitTelemetryExtensions.EndpointKey] = "http://127.0.0.1:1");
        host.Services.GetService<TracerProvider>().Should().NotBeNull();

        var reloj = Stopwatch.StartNew();
        for (var i = 0; i < 20; i++)
        {
            var response = await host.GetTestClient().GetAsync("/eco", TestContext.Current.CancellationToken);
            response.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        reloj.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5), "exportar se hace en segundo plano y no frena las peticiones");
    }

    private static async Task<WebApplication> StartAsync(Action<WebApplicationBuilder>? configure)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        configure?.Invoke(builder);
        builder.AddFlitTelemetry("prueba");

        var app = builder.Build();
        app.UseFlitCorrelationId();
        app.MapGet("/eco", (HttpContext ctx, ILoggerFactory loggers) =>
        {
            Procesando(loggers.CreateLogger("eco"), null);
            return ctx.Request.Headers[CorrelationIdMiddleware.HeaderName].ToString();
        });
        app.MapGet("/traza", () => Activity.Current?.GetTagItem(CorrelationIdMiddleware.TraceTag)?.ToString() ?? "sin-tag");
        await app.StartAsync(TestContext.Current.CancellationToken);
        return app;
    }

    /// <summary>Guarda los alcances activos de cada log, por categoría.</summary>
    private sealed class ScopeCapture : ILoggerProvider, ISupportExternalScope
    {
        private readonly Dictionary<string, Dictionary<string, object?>> _byCategory = new(StringComparer.Ordinal);
        private IExternalScopeProvider _scopes = new LoggerExternalScopeProvider();

        public Dictionary<string, object?> ScopesOf(string category)
        {
            lock (_byCategory)
                return _byCategory.TryGetValue(category, out var s) ? s : [];
        }

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(this, categoryName);

        public void SetScopeProvider(IExternalScopeProvider scopeProvider) => _scopes = scopeProvider;

        public void Dispose() { }

        private sealed class CapturingLogger(ScopeCapture owner, string category) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => owner._scopes.Push(state);

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                var found = new Dictionary<string, object?>(StringComparer.Ordinal);
                owner._scopes.ForEachScope((scope, acc) =>
                {
                    if (scope is IEnumerable<KeyValuePair<string, object>> pairs)
                    {
                        foreach (var (k, v) in pairs)
                            acc[k] = v;
                    }
                }, found);
                lock (owner._byCategory)
                    owner._byCategory[category] = found;
            }
        }
    }
}
