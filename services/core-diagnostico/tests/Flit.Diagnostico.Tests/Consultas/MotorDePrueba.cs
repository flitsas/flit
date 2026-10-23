using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Flit.Diagnostico.Api.Modulos.Configuracion;
using Flit.Diagnostico.Api.Modulos.Consultas;
using Flit.Diagnostico.Api.Modulos.Trabajos;
using Flit.Diagnostico.Domain.Consultas;
using Flit.Diagnostico.Tests.Aislamiento;
using Flit.Diagnostico.Tests.Api;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using static Flit.Diagnostico.Tests.Configuracion.ConfiguracionOperativaTests;

namespace Flit.Diagnostico.Tests.Consultas;

/// <summary>
/// Reloj manual (HU #13727): la expiración del lease se prueba avanzando el reloj, sin esperas reales. Con
/// <see cref="TimersManuales"/> (HU #13861) también sus timers son manuales: <c>Task.Delay(intervalo, reloj, ct)</c> solo
/// vence cuando <see cref="Avanzar"/> alcanza su hora; sin la opción, los timers son los del sistema (como antes).
/// </summary>
/// <example>
/// <code>
/// var reloj = new RelojManual { TimersManuales = true };
/// await reloj.EsperarTimersAsync(1, ct);
/// reloj.Avanzar(TimeSpan.FromHours(24));
/// </code>
/// </example>
public sealed class RelojManual : TimeProvider
{
    private readonly List<TimerManual> _timers = [];
    private long _ticks = DateTimeOffset.UtcNow.UtcTicks;

    /// <summary>Si los timers que crea este reloj vencen solo al avanzarlo (por defecto false: timers del sistema).</summary>
    public bool TimersManuales { get; init; }

    /// <summary>Timers manuales armados (con vencimiento pendiente).</summary>
    public int TimersArmados
    {
        get
        {
            lock (_timers)
                return _timers.Count(t => t.Armado);
        }
    }

    public override DateTimeOffset GetUtcNow() => new(Interlocked.Read(ref _ticks), TimeSpan.Zero);

    public void Avanzar(TimeSpan tiempo)
    {
        Interlocked.Add(ref _ticks, tiempo.Ticks);
        if (!TimersManuales)
            return;
        while (true)
        {
            TimerManual? vencido;
            lock (_timers)
                vencido = _timers.Where(t => t.Armado && t.Vence <= Interlocked.Read(ref _ticks)).MinBy(t => t.Vence);
            if (vencido is null)
                return;
            vencido.Disparar();
        }
    }

    /// <summary>Espera (en tiempo real, con tope de 10 s) a que haya <paramref name="cuantos"/> timers manuales armados.</summary>
    public async Task EsperarTimersAsync(int cuantos, CancellationToken ct)
    {
        var limite = DateTime.UtcNow.AddSeconds(10);
        while (TimersArmados < cuantos)
        {
            DateTime.UtcNow.Should().BeBefore(limite, "el trabajador debe armar su espera");
            await Task.Delay(5, ct);
        }
    }

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        if (!TimersManuales)
            return base.CreateTimer(callback, state, dueTime, period);
        var timer = new TimerManual(this, callback, state);
        lock (_timers)
            _timers.Add(timer);
        timer.Change(dueTime, period);
        return timer;
    }

    private void Quitar(TimerManual timer)
    {
        lock (_timers)
            _timers.Remove(timer);
    }

    private sealed class TimerManual(RelojManual reloj, TimerCallback callback, object? state) : ITimer
    {
        private TimeSpan _periodo = Timeout.InfiniteTimeSpan;

        public bool Armado { get; private set; }

        public long Vence { get; private set; }

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            lock (reloj._timers)
            {
                _periodo = period;
                Armado = dueTime != Timeout.InfiniteTimeSpan;
                Vence = reloj.GetUtcNow().UtcTicks + (Armado ? dueTime.Ticks : 0);
            }

            return true;
        }

        public void Disparar()
        {
            lock (reloj._timers)
            {
                if (_periodo == Timeout.InfiniteTimeSpan || _periodo <= TimeSpan.Zero)
                    Armado = false;
                else
                    Vence += _periodo.Ticks;
            }

            callback(state);
        }

        public void Dispose() => reloj.Quitar(this);

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}

/// <summary>
/// Ejecutor de la fuente RUNT para las pruebas del procesador (HU #13727): cuenta cada llamada por celda, anota la empresa
/// con que llegó el contexto y responde según <see cref="Comportamiento"/> (por defecto, completa con un objeto vacío).
/// </summary>
public sealed class EjecutorDePrueba : IEjecutorCelda
{
    private readonly ConcurrentDictionary<Guid, int> _llamadas = new();
    private readonly ConcurrentQueue<(Guid Celda, Guid Tenant, Guid? EmpresaDelContexto)> _contextos = new();

    public Fuente Fuente => Fuente.Runt;

    /// <summary>Lo que hace con cada celda; por defecto la completa tras ceder el hilo.</summary>
    public Func<ContextoCelda, CancellationToken, Task<ResultadoCelda>> Comportamiento { get; set; } = async (_, ct) =>
    {
        await Task.Delay(5, ct);
        return ResultadoCelda.Completado("{}", DateTimeOffset.UtcNow);
    };

    public IReadOnlyDictionary<Guid, int> Llamadas => _llamadas;

    public IReadOnlyCollection<(Guid Celda, Guid Tenant, Guid? EmpresaDelContexto)> Contextos => _contextos;

    public void Registrar(IServiceCollection servicios) => servicios.AddSingleton<IEjecutorCelda>(this);

    public Task<ResultadoCelda> EjecutarAsync(ContextoCelda contexto, CancellationToken ct)
    {
        _llamadas.AddOrUpdate(contexto.Celda.Id, 1, (_, n) => n + 1);
        _contextos.Enqueue((contexto.Celda.Id, contexto.Celda.TenantId, contexto.Db.EmpresaActual));
        return Comportamiento(contexto, ct);
    }
}

/// <summary>Ayudas del motor de consultas (HU #13727): lotes confirmados por la API, celdas por SQL y procesadores sueltos.</summary>
internal static class Motor
{
    /// <summary>Placas ficticias del rango de pruebas (TST###).</summary>
    public static Dictionary<string, object?>[] Placas(int cuantas, int desde = 1) =>
        [.. Enumerable.Range(desde, cuantas).Select(i => Lotes.Fila($"TST{i:000}"))];

    /// <summary>Siembra la configuración, crea la cabecera con el alcance dado y confirma <paramref name="filas"/> (202).</summary>
    public static async Task<Guid> LoteConfirmadoAsync(
        ApiDePrueba api, EmpresaDePrueba empresa, Dictionary<string, object?>[] filas, CancellationToken ct, string[]? porVehiculo = null,
        Fuente[]? habilitadas = null)
    {
        var id = await CabeceraAsync(api, empresa, ct, porVehiculo, habilitadas);
        var respuesta = await Lotes.ConfirmarAsync(api.Cliente(Cabeceras.TokenUsuario(api, empresa.Id)), id, filas, ct);
        respuesta.StatusCode.Should().Be(HttpStatusCode.Accepted, await respuesta.Content.ReadAsStringAsync(ct));
        return id;
    }

    /// <summary>Siembra la configuración y crea una cabecera SIMPLE en CREADO con el alcance por vehículo dado.</summary>
    public static async Task<Guid> CabeceraAsync(
        ApiDePrueba api, EmpresaDePrueba empresa, CancellationToken ct, string[]? porVehiculo = null, Fuente[]? habilitadas = null)
    {
        await SembrarAsync(empresa, new ConfiguracionEmpresa { FuentesHabilitadas = [.. habilitadas ?? [Fuente.Runt]] }, ct);
        var cliente = api.Cliente(Cabeceras.TokenUsuario(api, empresa.Id));
        var creada = await cliente.PostAsync(Cabeceras.Ruta, Cuerpo(Cabeceras.Json("Lote del motor", "SIMPLE", null, porVehiculo ?? ["RUNT"])), ct);
        creada.StatusCode.Should().Be(HttpStatusCode.Created, await creada.Content.ReadAsStringAsync(ct));
        return (await creada.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetGuid();
    }

    public static async Task<List<CeldaConsulta>> CeldasAsync(ApiDePrueba api, Guid empresa, Guid id, CancellationToken ct)
    {
        await using var db = DosEmpresas.Contexto(api.BaseDeDatos, empresa);
        return await db.Set<CeldaConsulta>().AsNoTracking().Where(c => c.DiagnosticoId == id).ToListAsync(ct);
    }

    /// <summary>Celdas de todas las empresas que no están COMPLETADO, por SQL directo.</summary>
    public static Task<long> SinCompletarAsync(ApiDePrueba api, CancellationToken ct) =>
        EscalarAsync(api, "SELECT count(*) FROM diagnostico.consultas WHERE estado <> 'COMPLETADO'", ct);

    public static async Task<long> EscalarAsync(ApiDePrueba api, string sql, CancellationToken ct)
    {
        await using var conexion = new Npgsql.NpgsqlConnection(api.BaseDeDatos);
        await conexion.OpenAsync(ct);
        await using var consulta = new Npgsql.NpgsqlCommand(sql, conexion);
        return (long)(await consulta.ExecuteScalarAsync(ct))!;
    }

    public static async Task EjecutarAsync(ApiDePrueba api, string sql, CancellationToken ct)
    {
        await using var conexion = new Npgsql.NpgsqlConnection(api.BaseDeDatos);
        await conexion.OpenAsync(ct);
        await using var comando = new Npgsql.NpgsqlCommand(sql, conexion);
        await comando.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// Un procesador suelto sobre la API (el trabajador hospedado está apagado en las pruebas). Sin
    /// <paramref name="maxReintentos"/> usa la configuración del servicio (<c>Diagnostico:Consultas:MaxReintentos</c>, HU #13737).
    /// </summary>
    public static ProcesadorConsultas Procesador(ApiDePrueba api, int tamanoLote = 10, int vehiculosPorEmpresa = 5, int? maxReintentos = null)
    {
        var procesador = Options.Create(new OpcionesProcesador
        {
            TamanoLote = tamanoLote,
            VehiculosEnVueloPorEmpresa = vehiculosPorEmpresa,
            DuracionLease = TimeSpan.FromMinutes(2),
        });
        return maxReintentos is { } max
            ? ActivatorUtilities.CreateInstance<ProcesadorConsultas>(api.App.Services, procesador, Options.Create(new OpcionesConsultas { MaxReintentos = max }))
            : ActivatorUtilities.CreateInstance<ProcesadorConsultas>(api.App.Services, procesador);
    }

    /// <summary>Procesa hasta que no quede celda sin completar (o vence el plazo, que hace fallar la prueba).</summary>
    public static async Task DrenarAsync(ApiDePrueba api, ProcesadorConsultas procesador, CancellationToken ct)
    {
        var limite = DateTime.UtcNow.AddSeconds(60);
        while (await SinCompletarAsync(api, ct) > 0)
        {
            DateTime.UtcNow.Should().BeBefore(limite, "la cola debe vaciarse");
            if (await procesador.ProcesarLoteAsync(ct) == 0)
                await Task.Delay(10, ct);
        }
    }
}
