using System.Diagnostics.Metrics;
using Flit.Consultas.Api.Configuracion;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Flit.Consultas.Tests;

/// <summary>
/// HU #14647 — la caché de 20 s de la contingencia RUNT, su fail-open, la relectura tras un guardado y la constancia de cada desvío.
/// Sin base de datos: la fuente y el reloj son falsos.
/// </summary>
public sealed class ContingenciaRuntVigenteTests
{
    private readonly RelojFalso _reloj = new();
    private readonly FuenteFalsa _fuente = new();
    private readonly RegistroLogs _logs = new();

    private ContingenciaRuntVigente Crear() => new(_fuente, _reloj, _logs);

    [Fact]
    public async Task Cache_NoLeeDeNuevoAntesDeLos20Segundos_YAlVencerRecarga()
    {
        _fuente.Valor = new Dictionary<string, string> { ["vehicle_plate"] = "verifik" };
        using var vigente = Crear();
        var ct = TestContext.Current.CancellationToken;

        (await vigente.ForzadoAsync("vehicle_plate", ct)).Should().Be("verifik");
        _fuente.Valor = new Dictionary<string, string>();
        _reloj.Avanzar(TimeSpan.FromSeconds(19));
        (await vigente.ForzadoAsync("vehicle_plate", ct)).Should().Be("verifik", "dentro de la vigencia no se vuelve a leer");
        _fuente.Lecturas.Should().Be(1);

        _reloj.Avanzar(TimeSpan.FromSeconds(2));
        (await vigente.ForzadoAsync("vehicle_plate", ct)).Should().BeNull("al vencer recarga y ve el cambio de otra réplica");
        _fuente.Lecturas.Should().Be(2);
    }

    [Fact]
    public async Task FailOpen_ConservaElUltimoValor_ReintentaALos5Segundos_YAlRecuperarseRecarga()
    {
        _fuente.Valor = new Dictionary<string, string> { ["conductor"] = "verifik_conductor" };
        using var vigente = Crear();
        var ct = TestContext.Current.CancellationToken;
        await vigente.ForzadoAsync("conductor", ct);
        _fuente.Falla = true;

        _reloj.Avanzar(ContingenciaRuntVigente.Vigencia + TimeSpan.FromSeconds(1));
        (await vigente.ForzadoAsync("conductor", ct)).Should().Be("verifik_conductor", "si la base falla se conserva el último valor");
        _fuente.Lecturas.Should().Be(2);
        _logs.Advertencias.Should().Be(1);

        _reloj.Avanzar(TimeSpan.FromSeconds(4));
        await vigente.ForzadoAsync("conductor", ct);
        _fuente.Lecturas.Should().Be(2, "no reintenta antes de los 5 s");

        _reloj.Avanzar(TimeSpan.FromSeconds(2));
        (await vigente.ForzadoAsync("conductor", ct)).Should().Be("verifik_conductor");
        _fuente.Lecturas.Should().Be(3, "a los 5 s reintenta");
        _logs.Advertencias.Should().Be(2);

        _fuente.Falla = false;
        _fuente.Valor = new Dictionary<string, string>();
        _reloj.Avanzar(ContingenciaRuntVigente.ReintentoTrasFallo + TimeSpan.FromSeconds(1));
        (await vigente.ForzadoAsync("conductor", ct)).Should().BeNull("recuperada la base, recarga y toma el valor nuevo");
    }

    [Fact]
    public async Task FailOpen_SinNingunValorPrevio_NoHayContingenciaYLaConsultaNoFalla()
    {
        _fuente.Falla = true;
        using var vigente = Crear();

        var forzado = await vigente.ForzadoAsync("vehicle_vin", TestContext.Current.CancellationToken);

        forzado.Should().BeNull();
    }

    [Fact]
    public async Task LecturaFallida_SumaLaMetrica()
    {
        long fallidas = 0;
        using var listener = new MeterListener();
        listener.InstrumentPublished = (i, l) =>
        {
            if (i.Meter.Name == ContingenciaRuntVigente.MeterName && i.Name == "consultas.contingencia.lecturas_fallidas")
                l.EnableMeasurementEvents(i);
        };
        listener.SetMeasurementEventCallback<long>((_, valor, _, _) => Interlocked.Add(ref fallidas, valor));
        listener.Start();
        _fuente.Falla = true;
        using var vigente = Crear();

        await vigente.ForzadoAsync("vehicle_plate", TestContext.Current.CancellationToken);

        Interlocked.Read(ref fallidas).Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task UnGuardadoMientrasSeLee_NoRepoNeElValorViejo()
    {
        var viejo = new Dictionary<string, string> { ["vehicle_plate"] = "kyverum_runt" };
        var nuevo = new Dictionary<string, string> { ["vehicle_plate"] = "verifik" };
        var enLectura = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var soltar = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _fuente.Valor = viejo;
        _fuente.Bloqueo = async () =>
        {
            enLectura.TrySetResult();
            await soltar.Task;
        };
        using var vigente = Crear();

        var consulta = vigente.ForzadoAsync("vehicle_plate", TestContext.Current.CancellationToken);
        await enLectura.Task;
        // Mientras la lectura vieja sigue en vuelo, otro hilo guarda: invalida y la base ya trae el valor nuevo.
        _fuente.Bloqueo = null;
        _fuente.Valor = nuevo;
        vigente.Invalidar();
        soltar.SetResult();

        (await consulta).Should().Be("verifik", "la lectura iniciada antes del guardado se descarta y se relee");
        (await vigente.ForzadoAsync("vehicle_plate", TestContext.Current.CancellationToken)).Should().Be("verifik");
    }

    [Fact]
    public void Desvio_DejaLogDeInformacionSinDatosDeLaConsulta_YSumaLaMetrica()
    {
        var etiquetas = new List<(string Tipo, string Proveedor, string Producto)>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (i, l) =>
        {
            if (i.Meter.Name == ContingenciaRuntVigente.MeterName && i.Name == "consultas.contingencia.desvios")
                l.EnableMeasurementEvents(i);
        };
        listener.SetMeasurementEventCallback<long>((_, _, tags, _) =>
        {
            var d = new Dictionary<string, string>();
            foreach (var t in tags)
                d[t.Key] = t.Value?.ToString() ?? string.Empty;
            lock (etiquetas)
                etiquetas.Add((d["tipo"], d["proveedor"], d["producto"]));
        });
        listener.Start();
        using var vigente = Crear();

        vigente.RegistrarDesvio("vehicle_plate", "verifik", "svc-tramites");

        etiquetas.Should().Contain(("vehicle_plate", "verifik", "tramites"));
        _logs.Informacion.Should().ContainSingle().Which.Should().Be("Consulta desviada por la contingencia RUNT: tipo=vehicle_plate, proveedor forzado=verifik, producto=tramites.");
    }

    private sealed class RelojFalso : TimeProvider
    {
        private DateTimeOffset _ahora = new(2026, 11, 19, 12, 0, 0, TimeSpan.Zero);

        public void Avanzar(TimeSpan t) => _ahora += t;

        public override DateTimeOffset GetUtcNow() => _ahora;
    }

    private sealed class FuenteFalsa : IFuenteContingenciaRunt
    {
        private int _lecturas;

        public IReadOnlyDictionary<string, string> Valor { get; set; } = new Dictionary<string, string>();

        public bool Falla { get; set; }

        public Func<Task>? Bloqueo { get; set; }

        public int Lecturas => _lecturas;

        public async Task<IReadOnlyDictionary<string, string>> LeerAsync(CancellationToken ct)
        {
            Interlocked.Increment(ref _lecturas);
            var valor = Valor;
            if (Bloqueo is { } bloqueo)
                await bloqueo();
            return Falla ? throw new InvalidOperationException("base caída") : valor;
        }
    }

    private sealed class RegistroLogs : ILogger<ContingenciaRuntVigente>
    {
        public int Advertencias { get; private set; }

        public List<string> Informacion { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Warning)
                Advertencias++;
            else if (logLevel == LogLevel.Information)
                Informacion.Add(formatter(state, exception));
        }
    }
}
