using Flit.Infrastructure.FlitoSignals;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace Flit.Infrastructure.Tests.FlitoSignals;

/// <summary>
/// HU #13625 (Feature #13618, Épica #13614) — configuración por ambiente de los avisos a FLITO.
/// Uso de ejemplo:
/// <code>
/// var o = new FlitoSignalsOptions { Enabled = true, TargetUrl = "https://dev.operaciones.flitsas.online/api/flito/sync/flit2/avisos", SigningKey = clave };
/// if (o.EsOperable(out var motivo)) { /* despachar */ }
/// </code>
/// </summary>
public sealed class FlitoSignalsOptionsTests
{
    private const string Destino = "https://dev.operaciones.flitsas.online/api/flito/sync/flit2/avisos";
    private const string ClaveFicticia = "clave-ficticia-de-prueba-0123456789abcdef";

    [Fact]
    public void AC1_EncendidoConUrlHttpsYClaveEsOperable()
    {
        var o = Opciones(new()
        {
            ["Integrations:FlitoSignals:Enabled"] = "true",
            ["Integrations:FlitoSignals:TargetUrl"] = Destino,
            ["Integrations:FlitoSignals:SigningKey"] = ClaveFicticia,
        });

        o.EsOperable(out var motivo).Should().BeTrue();
        motivo.Should().BeNull();
        o.TargetUrl.Should().Be(Destino);
        o.PollInterval.Should().Be(TimeSpan.FromSeconds(1));
        o.StabilityLag.Should().Be(TimeSpan.FromSeconds(5), "la ventana de estabilidad del feed v3.1");
    }

    [Theory]
    [InlineData(null, ClaveFicticia, "TargetUrl")]
    [InlineData("http://dev.operaciones.flitsas.online/api/flito/sync/flit2/avisos", ClaveFicticia, "TargetUrl")]
    [InlineData("/api/flito/sync/flit2/avisos", ClaveFicticia, "TargetUrl")]
    [InlineData(Destino, null, "SigningKey")]
    [InlineData(Destino, "   ", "SigningKey")]
    [InlineData(Destino, "corta", "SigningKey")]
    [InlineData(Destino, ClaveFicticia + "\n", "SigningKey")]
    [InlineData(Destino, " " + ClaveFicticia, "SigningKey")]
    public void AC2_EncendidoConConfiguracionIncompletaNoEsOperableYElMotivoNoIncluyeLaClave(string? url, string? clave, string campo)
    {
        var o = new FlitoSignalsOptions { Enabled = true, TargetUrl = url, SigningKey = clave };

        o.EsOperable(out var motivo).Should().BeFalse();
        motivo.Should().Contain(campo);
        if (!string.IsNullOrWhiteSpace(clave))
        {
            motivo.Should().NotContain(clave, "la clave nunca va a un mensaje que se registra");
        }
    }

    [Fact]
    public void AC2_ParametrosDeCicloInvalidosNoSonOperables()
    {
        new FlitoSignalsOptions { Enabled = true, TargetUrl = Destino, SigningKey = ClaveFicticia, BatchSize = 0 }
            .EsOperable(out var motivo).Should().BeFalse();
        motivo.Should().Contain("BatchSize");
    }

    [Fact]
    public void AC3_SinLaSeccionQuedaApagadoSinMotivoDeError()
    {
        var o = Opciones([]);

        o.Enabled.Should().BeFalse();
        o.EsOperable(out var motivo).Should().BeFalse();
        motivo.Should().BeNull("apagado no es un error de configuración");
    }

    [Fact]
    public async Task AC2_AlArrancarMalConfiguradoDejaUnErrorSinLaClaveYNoImpideElArranque()
    {
        var log = new LogCapturado();
        var check = new FlitoSignalsStartupCheck(
            Options.Create(new FlitoSignalsOptions { Enabled = true, TargetUrl = "http://inseguro.example/avisos", SigningKey = ClaveFicticia }),
            log.Crear<FlitoSignalsStartupCheck>());

        await FluentActions.Awaiting(() => check.StartAsync(CancellationToken.None)).Should().NotThrowAsync();

        log.Entradas.Should().ContainSingle(e => e.Nivel == LogLevel.Error && e.Mensaje.Contains("TargetUrl"));
        log.Entradas.Should().NotContain(e => e.Mensaje.Contains(ClaveFicticia));
    }

    [Fact]
    public async Task AC1_AlArrancarEncendidoRegistraSoloElHost()
    {
        var log = new LogCapturado();
        var check = new FlitoSignalsStartupCheck(
            Options.Create(new FlitoSignalsOptions { Enabled = true, TargetUrl = Destino, SigningKey = ClaveFicticia }),
            log.Crear<FlitoSignalsStartupCheck>());

        await check.StartAsync(CancellationToken.None);

        log.Entradas.Should().ContainSingle(e => e.Nivel == LogLevel.Information && e.Mensaje.Contains("dev.operaciones.flitsas.online"));
        log.Entradas.Should().NotContain(e => e.Mensaje.Contains(ClaveFicticia) || e.Mensaje.Contains("/api/flito"));
    }

    [Fact]
    public async Task AC3_AlArrancarApagadoLoDejaEnElLogComoInformacion()
    {
        var log = new LogCapturado();
        var check = new FlitoSignalsStartupCheck(Options.Create(new FlitoSignalsOptions()), log.Crear<FlitoSignalsStartupCheck>());

        await check.StartAsync(CancellationToken.None);

        log.Entradas.Should().ContainSingle(e => e.Nivel == LogLevel.Information && e.Mensaje.Contains("apagados"));
    }

    private static FlitoSignalsOptions Opciones(Dictionary<string, string?> valores)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(valores).Build();
        var services = new ServiceCollection();
        services.Configure<FlitoSignalsOptions>(configuration.GetSection(FlitoSignalsOptions.SectionName));
        return services.BuildServiceProvider().GetRequiredService<IOptions<FlitoSignalsOptions>>().Value;
    }

    private sealed class LogCapturado : ILoggerProvider
    {
        public List<(LogLevel Nivel, string Mensaje)> Entradas { get; } = [];

        public ILogger<T> Crear<T>() => new LoggerFactory([this]).CreateLogger<T>();

        public ILogger CreateLogger(string categoryName) => new Registro(Entradas);

        public void Dispose()
        {
        }

        private sealed class Registro(List<(LogLevel, string)> entradas) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
                entradas.Add((logLevel, formatter(state, exception)));
        }
    }
}
