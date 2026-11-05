using System.Threading.Channels;
using Flit.Diagnostico.Api.Modulos.Catalogos;
using Flit.Diagnostico.Tests.Consultas;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Flit.Diagnostico.Tests.Catalogos;

/// <summary>
/// HU #13861 (F09-H2), AC3 (F09-C2) — el trabajador de la réplica de departamentos con impuesto sincroniza al arrancar y cada
/// <c>Diagnostico:Impuesto:IntervaloSincronizacion</c> (24 h por defecto; ≤ 0 usa el de defecto) con
/// <c>Task.Delay(intervalo, reloj, ct)</c>; una excepción se registra con su EventId y se reintenta en la siguiente vuelta sin
/// tumbar el servicio. Sin esperas reales: <see cref="RelojManual"/> con timers manuales. Sin base: la sincronización es un doble.
/// </summary>
/// <example>
/// <code>
/// dotnet test services/core-diagnostico/tests/Flit.Diagnostico.Tests --no-build --filter FullyQualifiedName~TrabajadorSincronizacionDepartamentosImpuestoTests
/// </code>
/// </example>
public sealed class TrabajadorSincronizacionDepartamentosImpuestoTests
{
    // Al arrancar sincroniza una vez; luego una vez por intervalo del reloj, nunca antes.
    [Fact]
    public async Task Sincroniza_al_arrancar_y_en_cada_intervalo()
    {
        var ct = TestContext.Current.CancellationToken;
        var reloj = new RelojManual { TimersManuales = true };
        var sincronizacion = new SincronizacionContada();
        using var trabajador = Trabajador(sincronizacion, reloj, "01:00:00", new LogsConEvento());

        await trabajador.StartAsync(ct);
        await sincronizacion.EsperarAsync(1, ct);
        await reloj.EsperarTimersAsync(1, ct);
        reloj.Avanzar(TimeSpan.FromMinutes(59));
        sincronizacion.Llamadas.Should().Be(1, "el intervalo aún no se cumple");

        reloj.Avanzar(TimeSpan.FromMinutes(1));
        await sincronizacion.EsperarAsync(2, ct);
        await reloj.EsperarTimersAsync(1, ct);
        reloj.Avanzar(TimeSpan.FromHours(1));
        await sincronizacion.EsperarAsync(3, ct);
        await trabajador.StopAsync(ct);

        sincronizacion.Llamadas.Should().Be(3);
    }

    // Un intervalo cero o negativo usa el de defecto (24 horas).
    [Theory]
    [InlineData("00:00:00")]
    [InlineData("-01:00:00")]
    public async Task Intervalo_no_positivo_usa_24_horas(string intervalo)
    {
        var ct = TestContext.Current.CancellationToken;
        var reloj = new RelojManual { TimersManuales = true };
        var sincronizacion = new SincronizacionContada();
        using var trabajador = Trabajador(sincronizacion, reloj, intervalo, new LogsConEvento());

        await trabajador.StartAsync(ct);
        await sincronizacion.EsperarAsync(1, ct);
        await reloj.EsperarTimersAsync(1, ct);
        reloj.Avanzar(TimeSpan.FromHours(23));
        sincronizacion.Llamadas.Should().Be(1);
        reloj.Avanzar(TimeSpan.FromHours(1));
        await sincronizacion.EsperarAsync(2, ct);
        await trabajador.StopAsync(ct);
    }

    // AC3 — una excepción en la vuelta se registra (EventId propio, Warning, con el tipo) y la siguiente vuelta reintenta.
    [Fact]
    public async Task Una_excepcion_se_registra_y_se_reintenta_en_la_siguiente_vuelta()
    {
        var ct = TestContext.Current.CancellationToken;
        var reloj = new RelojManual { TimersManuales = true };
        var sincronizacion = new SincronizacionContada { FallarEnLaPrimera = true };
        var logs = new LogsConEvento();
        using var trabajador = Trabajador(sincronizacion, reloj, "1.00:00:00", logs);

        await trabajador.StartAsync(ct);
        await sincronizacion.EsperarAsync(1, ct);
        await reloj.EsperarTimersAsync(1, ct);
        logs.Eventos.Should().ContainSingle(e => e.EventId == TrabajadorSincronizacionDepartamentosImpuesto.EventoSincronizacionFallida)
            .Which.Should().Match<(int EventId, LogLevel Nivel, string Mensaje)>(e => e.Nivel == LogLevel.Warning && e.Mensaje.Contains(nameof(InvalidOperationException)));
        trabajador.ExecuteTask!.IsCompleted.Should().BeFalse("una falla no tumba el servicio");

        reloj.Avanzar(TimeSpan.FromHours(24));
        await sincronizacion.EsperarAsync(2, ct);
        await trabajador.StopAsync(ct);

        sincronizacion.Exitosas.Should().Be(1, "la siguiente vuelta reintenta y sincroniza");
        logs.Eventos.Count(e => e.EventId == TrabajadorSincronizacionDepartamentosImpuesto.EventoSincronizacionFallida).Should().Be(1);
    }

    private static TrabajadorSincronizacionDepartamentosImpuesto Trabajador(
        SincronizacionContada sincronizacion, RelojManual reloj, string intervalo, LogsConEvento logs)
    {
        var servicios = new ServiceCollection();
        servicios.AddScoped<ISincronizacionDepartamentosImpuesto>(_ => sincronizacion);
        var configuracion = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [TrabajadorSincronizacionDepartamentosImpuesto.ClaveIntervalo] = intervalo })
            .Build();
        return new TrabajadorSincronizacionDepartamentosImpuesto(
            servicios.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(), configuracion, reloj,
            logs.Logger<TrabajadorSincronizacionDepartamentosImpuesto>());
    }

    /// <summary>Sincronización de prueba: cuenta cada vuelta, avisa por un canal y, si se pide, falla la primera.</summary>
    private sealed class SincronizacionContada : ISincronizacionDepartamentosImpuesto
    {
        private readonly Channel<int> _vueltas = Channel.CreateUnbounded<int>();
        private int _llamadas;
        private int _exitosas;
        private int _vistas;

        public bool FallarEnLaPrimera { get; init; }

        public int Llamadas => Volatile.Read(ref _llamadas);

        public int Exitosas => Volatile.Read(ref _exitosas);

        public Task<ResultadoSincronizacionImpuesto?> SincronizarAsync(CancellationToken ct)
        {
            var n = Interlocked.Increment(ref _llamadas);
            _vueltas.Writer.TryWrite(n);
            if (FallarEnLaPrimera && n == 1)
                throw new InvalidOperationException("sincronización de prueba");
            Interlocked.Increment(ref _exitosas);
            return Task.FromResult<ResultadoSincronizacionImpuesto?>(new ResultadoSincronizacionImpuesto(0, 0, 0, 0, 0));
        }

        /// <summary>Espera (con tope de 10 s) a que la vuelta número <paramref name="n"/> haya ocurrido.</summary>
        public async Task EsperarAsync(int n, CancellationToken ct)
        {
            using var tope = CancellationTokenSource.CreateLinkedTokenSource(ct);
            tope.CancelAfter(TimeSpan.FromSeconds(10));
            while (_vistas < n)
                _vistas = await _vueltas.Reader.ReadAsync(tope.Token);
        }
    }
}
