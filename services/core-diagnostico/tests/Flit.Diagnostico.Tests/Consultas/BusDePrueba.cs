using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Flit.Diagnostico.Api.Modulos.Configuracion;
using Flit.Diagnostico.Api.Modulos.Consultas;
using Flit.Diagnostico.Api.Modulos.Consultas.Bus;
using Flit.Diagnostico.Api.Persistence;
using Flit.Diagnostico.Domain.Consultas;
using Flit.Diagnostico.Tests.Aislamiento;
using Flit.Diagnostico.Tests.Api;
using Flit.Platform.Sdk.Messaging;
using FluentAssertions;
using Google.Protobuf;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using static Flit.Diagnostico.Tests.Configuracion.ConfiguracionOperativaTests;

namespace Flit.Diagnostico.Tests.Consultas;

/// <summary>
/// Reloj manual (HU #13727): las esperas (backoff, márgenes del reconciliador) se prueban avanzando el reloj, sin esperas reales.
/// Con <see cref="TimersManuales"/> (HU #13861) también sus timers son manuales: <c>Task.Delay(intervalo, reloj, ct)</c> solo vence
/// cuando <see cref="Avanzar"/> alcanza su hora; sin la opción, los timers son los del sistema (como antes).
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

/// <summary>Un trabajo de consulta que Diagnóstico dejó en su outbox hacia <c>flit.consultas</c>, tal como lo atiende el bus de prueba.</summary>
public sealed record TrabajoAtendido(Guid OutboxId, string RoutingKey, EventEnvelope Sobre)
{
    public Guid TrabajoId => Sobre.Data.GetProperty("trabajoId").GetGuid();

    public Guid ConsultaId => Sobre.Data.GetProperty("consultaId").GetGuid();

    public Guid? PersonaId => Sobre.Data.GetProperty("personaId") is { ValueKind: JsonValueKind.String } p ? p.GetGuid() : null;

    public string Fuente => Sobre.Data.GetProperty("fuente").GetString()!;

    public string Rpc => Sobre.Data.GetProperty("rpc").GetString()!;

    public JsonElement Solicitud => Sobre.Data.GetProperty("solicitud");
}

/// <summary>
/// Lo que core-consultas responde a un trabajo en las pruebas: el <c>*Response</c> (estado OK; null = respuesta ilegible) o el
/// código gRPC (estado ERROR).
/// </summary>
public sealed record RespuestaSimulada(IMessage? Respuesta, StatusCode? Error = null, bool DesdeCache = false, long DuracionMs = 0)
{
    public static RespuestaSimulada Ok(IMessage respuesta) => new(respuesta);

    public static RespuestaSimulada Fallo(StatusCode codigo) => new(null, codigo);
}

/// <summary>
/// El bus de las pruebas de Diagnóstico (ADR-0075, T9; reemplaza al procesador de Postgres): en las pruebas el broker es
/// inalcanzable, así que <see cref="ProcesarLoteAsync"/> drena la outbox de ida (<c>flit.consultas</c>), simula a core-consultas
/// (<see cref="ConsultasSimuladas"/>, o <see cref="Responder"/>) y entrega cada resultado <c>consultas.resultado.diagnostico</c> al
/// <see cref="ConsumidorResultados"/> real como lo haría el SDK: un scope por mensaje, la transacción abierta antes del efecto y
/// revertida si lanza, y la bandeja (un <c>eventId</c> repetido no repite el efecto). <see cref="ReintentoProgramadoException"/> (o un
/// conflicto de concurrencia) lleva el resultado a la «cola de reintentos» con las esperas del consumidor
/// (<see cref="ProgramadorReintentos.Esperas"/>) medidas con el reloj de la API; agotadas, a <see cref="Muertos"/>. Cualquier otra
/// excepción hace fallar la prueba. Los resultados demorados se entregan cuando el reloj alcanza su vencimiento.
/// </summary>
/// <example>
/// <code>
/// var bus = Motor.Bus(api, maxReintentos: 1);
/// await bus.ProcesarTodoAsync(ct);                    // atiende trabajos hasta que no quede nada listo
/// bus.Atendidos.Should().OnlyContain(t => t.Fuente == "runt");
/// </code>
/// </example>
public sealed class BusDePrueba
{
    private readonly ApiDePrueba _api;
    private readonly int? _maxReintentos;
    private readonly HashSet<Guid> _vistos = [];
    private readonly List<(EventEnvelope Sobre, int Intento, DateTimeOffset Vence)> _demorados = [];
    private readonly ConcurrentQueue<TrabajoAtendido> _atendidos = new();
    private readonly ConcurrentQueue<EventEnvelope> _muertos = new();

    public BusDePrueba(ApiDePrueba api, int? maxReintentos = null)
    {
        _api = api;
        _maxReintentos = maxReintentos;
    }

    /// <summary>Los trabajos que atendió el bus, en orden.</summary>
    public IReadOnlyCollection<TrabajoAtendido> Atendidos => _atendidos;

    /// <summary>Resultados que agotaron la cola de reintentos (la DLQ).</summary>
    public IReadOnlyCollection<EventEnvelope> Muertos => _muertos;

    /// <summary>Resultados que esperan en la cola de reintentos.</summary>
    public int Demorados
    {
        get
        {
            lock (_demorados)
                return _demorados.Count;
        }
    }

    /// <summary>Respuesta propia de la prueba para un trabajo; null = la de <see cref="ConsultasSimuladas"/>.</summary>
    public Func<TrabajoAtendido, RespuestaSimulada?>? Responder { get; set; }

    private TimeProvider Reloj => _api.App.Services.GetRequiredService<TimeProvider>();

    /// <summary>
    /// Una vuelta: entrega los resultados demorados ya vencidos y atiende los trabajos que hay en la outbox de ida; devuelve cuántos
    /// trabajos atendió (0 si no había ninguno).
    /// </summary>
    public async Task<int> ProcesarLoteAsync(CancellationToken ct)
    {
        await EntregarDemoradosAsync(ct);
        var atendidos = 0;
        foreach (var trabajo in await TrabajosNuevosAsync(ct))
        {
            atendidos++;
            _atendidos.Enqueue(trabajo);
            var respuesta = Responder?.Invoke(trabajo) ?? await ConsultasSimuladas.ResponderAsync(_api, trabajo, ct);
            await EntregarAsync(Resultado(trabajo, respuesta), ct);
        }

        return atendidos;
    }

    /// <summary>Entrega solo los resultados demorados ya vencidos (sin atender trabajos nuevos); devuelve cuántos entregó.</summary>
    public async Task<int> EntregarDemoradosAsync(CancellationToken ct)
    {
        List<(EventEnvelope Sobre, int Intento, DateTimeOffset Vence)> vencidos;
        lock (_demorados)
        {
            var ahora = Reloj.GetUtcNow();
            vencidos = [.. _demorados.Where(d => d.Vence <= ahora).OrderBy(d => d.Vence)];
            foreach (var d in vencidos)
                _demorados.Remove(d);
        }

        foreach (var (sobre, intento, _) in vencidos)
            await EntregarAsync(sobre, ct, intento);
        return vencidos.Count;
    }

    /// <summary>Vueltas hasta que no quede trabajo listo (los resultados demorados que no vencen quedan esperando).</summary>
    public async Task ProcesarTodoAsync(CancellationToken ct)
    {
        for (var vuelta = 0; vuelta < 50; vuelta++)
        {
            if (await ProcesarLoteAsync(ct) == 0)
                return;
        }

        throw new InvalidOperationException("El bus no se vació en 50 vueltas.");
    }

    /// <summary>Marca como ya atendidos los trabajos que hay en la outbox (sin responderlos): simulan trabajos perdidos.</summary>
    public async Task<IReadOnlyList<TrabajoAtendido>> DescartarTrabajosAsync(CancellationToken ct) => await TrabajosNuevosAsync(ct);

    /// <summary>
    /// El resultado de <paramref name="trabajo"/> con <paramref name="respuesta"/>, como lo publica core-consultas desde HU #14087 AC8:
    /// la respuesta completa en proto-JSON con los nombres del .proto y los valores por defecto (<see cref="FormatoConsultas"/>).
    /// </summary>
    /// <summary>El formato de <c>PublicadorResultado</c> de core-consultas (HU #14087 AC8).</summary>
    public static JsonFormatter FormatoConsultas { get; } =
        new(JsonFormatter.Settings.Default.WithFormatDefaultValues(true).WithPreserveProtoFieldNames(true));

    public EventEnvelope Resultado(TrabajoAtendido trabajo, RespuestaSimulada respuesta)
    {
        ArgumentNullException.ThrowIfNull(trabajo);
        ArgumentNullException.ThrowIfNull(respuesta);
        JsonElement? json = null;
        if (respuesta.Error is null && respuesta.Respuesta is { } mensaje)
        {
            using var documento = JsonDocument.Parse(FormatoConsultas.Format(mensaje));
            json = documento.RootElement.Clone();
        }

        var data = new ResultadoConsultaDiagnostico(
            trabajo.TrabajoId, trabajo.ConsultaId, trabajo.PersonaId, trabajo.Fuente,
            respuesta.Error is null ? ResultadoConsultaDiagnostico.EstadoOk : ResultadoConsultaDiagnostico.EstadoError,
            respuesta.Error?.ToString(), json, respuesta.DuracionMs, respuesta.DesdeCache);
        return new EventEnvelope(
            Guid.CreateVersion7(), ResultadoConsultaDiagnostico.Tipo, ResultadoConsultaDiagnostico.VersionContrato, Reloj.GetUtcNow(),
            trabajo.Sobre.TenantId, OpcionesBus.Productor, trabajo.Sobre.CorrelationId, JsonSerializer.SerializeToElement(data, EventEnvelope.JsonOptions));
    }

    /// <summary>
    /// Entrega un resultado al consumidor como el SDK (scope, transacción, bandeja). Devuelve false si el SDK lo habría dejado en la
    /// cola de reintentos (o en la DLQ) y true si se confirmó.
    /// </summary>
    public async Task<bool> EntregarAsync(EventEnvelope sobre, CancellationToken ct, int intento = 0)
    {
        ArgumentNullException.ThrowIfNull(sobre);
        try
        {
            await ProcesarAsync(sobre, ct);
            return true;
        }
        catch (Exception ex) when (ex is ReintentoProgramadoException or DbUpdateException)
        {
            var esperas = ProgramadorReintentos.Esperas;
            if (intento < esperas.Count)
            {
                lock (_demorados)
                    _demorados.Add((sobre, intento + 1, Reloj.GetUtcNow() + esperas[intento]));
            }
            else
            {
                _muertos.Enqueue(sobre);
            }

            return false;
        }
    }

    private async Task ProcesarAsync(EventEnvelope sobre, CancellationToken ct)
    {
        await using var scope = _api.App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DiagnosticoDb>();
        var consumidor = _maxReintentos is { } max
            ? ActivatorUtilities.CreateInstance<ConsumidorResultados>(scope.ServiceProvider, new ProgramadorReintentos(
                scope.ServiceProvider.GetRequiredService<IServiceScopeFactory>(), Reloj,
                Options.Create(new OpcionesConsultas { MaxReintentos = max }), NullLogger<ProgramadorReintentos>.Instance))
            : scope.ServiceProvider.GetRequiredService<ConsumidorResultados>();
        await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var transaccion = await db.Database.BeginTransactionAsync(ct);
            if (await db.Set<InboxMessage>().AnyAsync(m => m.EventId == sobre.EventId && m.Consumer == OpcionesBus.Cola, ct))
                return;
            await consumidor.HandleAsync(sobre, sobre.DataAs<ResultadoConsultaDiagnostico>(), ct);
            db.Set<InboxMessage>().Add(new InboxMessage { EventId = sobre.EventId, Consumer = OpcionesBus.Cola, Type = sobre.Type, ProcessedAt = Reloj.GetUtcNow() });
            await db.SaveChangesAsync(ct);
            await transaccion.CommitAsync(ct);
        });
    }

    /// <summary>
    /// Los trabajos de la outbox que nadie publicó todavía; quedan sellados (<c>published_at</c>), como los deja el publicador del
    /// SDK al confirmarlos el broker, así que otro bus de la misma prueba no los vuelve a atender.
    /// </summary>
    private async Task<List<TrabajoAtendido>> TrabajosNuevosAsync(CancellationToken ct)
    {
        await using var db = DosEmpresas.Contexto(_api.BaseDeDatos, null);
        var filas = await db.Set<OutboxMessage>().AsNoTracking()
            .Where(m => m.Exchange == "flit.consultas" && m.PublishedAt == null).OrderBy(m => m.OccurredAt).ThenBy(m => m.Id).ToListAsync(ct);
        var nuevos = new List<TrabajoAtendido>();
        foreach (var fila in filas)
        {
            if (_vistos.Add(fila.Id))
                nuevos.Add(new TrabajoAtendido(fila.Id, fila.RoutingKey, EventEnvelope.FromJson(Encoding.UTF8.GetBytes(fila.Payload))));
        }

        if (nuevos.Count > 0)
        {
            var ids = nuevos.Select(t => t.OutboxId).ToList();
            var ahora = Reloj.GetUtcNow().ToUniversalTime();
            await db.Set<OutboxMessage>().Where(m => ids.Contains(m.Id)).ExecuteUpdateAsync(s => s.SetProperty(m => m.PublishedAt, ahora), ct);
        }

        return nuevos;
    }
}

/// <summary>
/// Ejecutor de prueba (ADR-0075): un doble de <see cref="IEjecutorCelda"/> que decide la celda con <see cref="EjecutarAsync"/> tanto
/// si su constructor no armó consulta como cuando llega la respuesta de core-consultas (las pruebas del motor no miran la
/// respuesta: solo el orden, la empresa y el resultado que deciden).
/// </summary>
public abstract class EjecutorSimple : IEjecutorCelda
{
    public abstract Fuente Fuente { get; }

    public abstract Task<ResultadoCelda> EjecutarAsync(ContextoCelda contexto, CancellationToken ct);

    public Task<ResultadoCelda> SinConsultaAsync(ContextoCelda contexto, CancellationToken ct) => EjecutarAsync(contexto, ct);

    public Task<ResultadoCelda> AplicarAsync(ContextoCelda contexto, RespuestaConsulta respuesta, CancellationToken ct) => EjecutarAsync(contexto, ct);
}

/// <summary>
/// Ejecutor de la fuente RUNT para las pruebas del motor (HU #13727): cuenta cada respuesta por celda, anota la empresa con que
/// llegó el contexto y responde según <see cref="Comportamiento"/> (por defecto, completa con un objeto vacío).
/// </summary>
public sealed class EjecutorDePrueba : EjecutorSimple
{
    private readonly ConcurrentDictionary<Guid, int> _llamadas = new();
    private readonly ConcurrentQueue<(Guid Celda, Guid Tenant, Guid? EmpresaDelContexto)> _contextos = new();

    public override Fuente Fuente => Fuente.Runt;

    /// <summary>Lo que hace con cada celda; por defecto la completa tras ceder el hilo.</summary>
    public Func<ContextoCelda, CancellationToken, Task<ResultadoCelda>> Comportamiento { get; set; } = async (_, ct) =>
    {
        await Task.Delay(5, ct);
        return ResultadoCelda.Completado("{}", DateTimeOffset.UtcNow);
    };

    public IReadOnlyDictionary<Guid, int> Llamadas => _llamadas;

    public IReadOnlyCollection<(Guid Celda, Guid Tenant, Guid? EmpresaDelContexto)> Contextos => _contextos;

    public void Registrar(IServiceCollection servicios) => servicios.AddSingleton<IEjecutorCelda>(this);

    public override Task<ResultadoCelda> EjecutarAsync(ContextoCelda contexto, CancellationToken ct)
    {
        _llamadas.AddOrUpdate(contexto.Celda.Id, 1, (_, n) => n + 1);
        _contextos.Enqueue((contexto.Celda.Id, contexto.Celda.TenantId, contexto.Db.EmpresaActual));
        return Comportamiento(contexto, ct);
    }
}

/// <summary>Ayudas del motor de consultas (HU #13727): lotes confirmados por la API, celdas por SQL y el bus de prueba.</summary>
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
    /// El bus de prueba sobre la API (el consumidor hospedado está apagado en las pruebas). Sin <paramref name="maxReintentos"/> usa
    /// la configuración del servicio (<c>Diagnostico:Consultas:MaxReintentos</c>, HU #13737).
    /// </summary>
    public static BusDePrueba Bus(ApiDePrueba api, int? maxReintentos = null) => new(api, maxReintentos);
}
