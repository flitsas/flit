using System.Collections.Concurrent;
using System.Net;
using Flit.Impuestos.Api.Plataforma;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Flit.Impuestos.Tests.Seguridad;

/// <summary>
/// HU #14694 (M05, EP-CF3a) — el log redacta <c>X-API-Key</c>, <c>Authorization</c> y <c>X-Flit-Signature</c>. Se prueba con el
/// registro real de los <c>HttpClient</c> del servicio (<see cref="RedaccionCabeceras.AddRedaccionCabeceras"/>): el log de nivel
/// Trace de una petición con esas cabeceras no contiene sus valores, pero sí el de una cabecera que no es secreta.
/// </summary>
/// <example>
/// <code>
/// services.AddRedaccionCabeceras();
/// RedaccionCabeceras.Redactar("X-API-Key", "valor"); // "[REDACTADO]"
/// </code>
/// </example>
public sealed class RedaccionCabecerasTests
{
    private const string ApiKey = "clave-de-prueba-no-real-1";
    private const string Bearer = "Bearer token-de-prueba-no-real-2";
    private const string Firma = "t=1,v1=firma-de-prueba-no-real-3";
    private const string Correlacion = "correlacion-visible-4";

    [Theory]
    [InlineData("X-API-Key")]
    [InlineData("x-api-key")]
    [InlineData("Authorization")]
    [InlineData("AUTHORIZATION")]
    [InlineData("X-Flit-Signature")]
    [InlineData("x-flit-signature")]
    public void Las_tres_cabeceras_son_sensibles_sin_importar_mayusculas(string cabecera)
    {
        RedaccionCabeceras.EsSensible(cabecera).Should().BeTrue();
        RedaccionCabeceras.Redactar(cabecera, "valor-secreto").Should().Be(RedaccionCabeceras.Redactado);
    }

    [Theory]
    [InlineData("X-Correlation-Id")]
    [InlineData("Content-Type")]
    [InlineData("X-Flit-Delivery")]
    public void Las_demas_cabeceras_no_se_redactan(string cabecera)
    {
        RedaccionCabeceras.EsSensible(cabecera).Should().BeFalse();
        RedaccionCabeceras.Redactar(cabecera, "valor").Should().Be("valor");
    }

    [Fact]
    public void La_lista_de_sensibles_es_la_de_EP_CF3a()
    {
        RedaccionCabeceras.Sensibles.Should().BeEquivalentTo(["X-API-Key", "Authorization", "X-Flit-Signature"]);
    }

    [Fact]
    public async Task El_log_de_los_HttpClient_del_servicio_no_lleva_los_valores_secretos()
    {
        var ct = TestContext.Current.CancellationToken;
        var registro = new RegistroEnMemoria();
        var services = new ServiceCollection();
        services.AddLogging(l => l.SetMinimumLevel(LogLevel.Trace).AddProvider(registro));
        services.AddRedaccionCabeceras();
        services.AddHttpClient("api-liquidacion").ConfigurePrimaryHttpMessageHandler(() => new Responde200());
        await using var provider = services.BuildServiceProvider();
        using var cliente = provider.GetRequiredService<IHttpClientFactory>().CreateClient("api-liquidacion");
        using var pedido = new HttpRequestMessage(HttpMethod.Post, "http://api.prueba/api/v1/consultas");
        pedido.Headers.TryAddWithoutValidation("X-API-Key", ApiKey);
        pedido.Headers.TryAddWithoutValidation("Authorization", Bearer);
        pedido.Headers.TryAddWithoutValidation("X-Flit-Signature", Firma);
        pedido.Headers.TryAddWithoutValidation("X-Correlation-Id", Correlacion);

        using var respuesta = await cliente.SendAsync(pedido, ct);

        var log = string.Join("\n", registro.Lineas);
        respuesta.StatusCode.Should().Be(HttpStatusCode.OK);
        log.Should().Contain(Correlacion, "el log sí registra las cabeceras (si no, la prueba no prueba nada)");
        log.Should().Contain("X-API-Key").And.Contain("X-Flit-Signature");
        log.Should().NotContain(ApiKey).And.NotContain("token-de-prueba-no-real-2").And.NotContain("firma-de-prueba-no-real-3");
    }

    private sealed class Responde200 : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") });
    }

    private sealed class RegistroEnMemoria : ILoggerProvider
    {
        public ConcurrentQueue<string> Lineas { get; } = new();

        public ILogger CreateLogger(string categoryName) => new Logger(Lineas);

        public void Dispose()
        {
        }

        private sealed class Logger(ConcurrentQueue<string> lineas) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
                lineas.Enqueue(formatter(state, exception));
        }
    }
}
