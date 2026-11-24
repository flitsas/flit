using System.Collections.Concurrent;
using Flit.Consultas.Api.Impuesto;
using Flit.Tramites.Application.UseCases.Consultations;
using Microsoft.Extensions.Logging;

namespace Flit.Consultas.Tests.Impuesto;

/// <summary>
/// Dobles del impuesto asíncrono (diseño §2.1). Datos sintéticos: ninguna placa, documento, nombre ni llave real.
/// <para>Uso de ejemplo: <c>new TransporteGuionado { Estados = { Estados.EnCola(), Estados.Terminal("succeeded", json) } }</c>.</para>
/// </summary>
internal sealed class TransporteGuionado : ITransporteImpuesto
{
    public bool Listo { get; init; } = true;

    public string NombreProveedor => "impuesto";

    public Queue<EnvioImpuesto> Envios { get; } = new();

    public Queue<EstadoImpuesto> Estados { get; } = new();

    public Queue<ArchivoImpuesto> Archivos { get; } = new();

    public List<string> CuerposEnviados { get; } = [];

    public List<string> LlavesUsadas { get; } = [];

    public List<string> IdsConsultados { get; } = [];

    public List<string> ArchivosPedidos { get; } = [];

    public int Posts => CuerposEnviados.Count;

    public int Gets => IdsConsultados.Count;

    public Task<EnvioImpuesto> EnviarAsync(string cuerpo, string apiKey, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        CuerposEnviados.Add(cuerpo);
        LlavesUsadas.Add(apiKey);
        return Task.FromResult(Envios.Count > 0 ? Envios.Dequeue() : new EnvioImpuesto("ext-" + Posts, null, null));
    }

    public Task<EstadoImpuesto> ConsultarAsync(string idExterno, string apiKey, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        IdsConsultados.Add(idExterno);
        LlavesUsadas.Add(apiKey);
        return Task.FromResult(Estados.Count > 0 ? Estados.Dequeue() : new EstadoImpuesto(EstadoTrabajoExterno.Ejecutando, null, null, null));
    }

    public Task<ArchivoImpuesto> DescargarAsync(string archivoId, string apiKey, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        ArchivosPedidos.Add(archivoId);
        LlavesUsadas.Add(apiKey);
        return Task.FromResult(Archivos.Count > 0 ? Archivos.Dequeue() : new ArchivoImpuesto(null, null, null, true));
    }
}

internal static class Guion
{
    public static EstadoImpuesto EnCola() => new(EstadoTrabajoExterno.EnCola, null, null, null);

    public static EstadoImpuesto Ejecutando() => new(EstadoTrabajoExterno.Ejecutando, null, null, null);

    public static EstadoImpuesto Terminal(EstadoTrabajoExterno estado, string cuerpo) => new(estado, cuerpo, null, null);

    public static EstadoImpuesto Fallo(ClaseFalloProveedor clase, int? http = null) => new(EstadoTrabajoExterno.Desconocido, null, new FalloProveedor(clase, http), null);

    public static EstadoImpuesto NoEncontrado() => new(EstadoTrabajoExterno.NoEncontrado, null, new FalloProveedor(ClaseFalloProveedor.Caida, 404), "not_found");

    public static EnvioImpuesto Rechazo(ClaseFalloProveedor clase, int http, string codigo) => new(null, new FalloProveedor(clase, http), codigo);

    public const string PazYSalvo = """
        {"id":"ext-1","status":"succeeded","result":{"status":"paz_y_salvo","totals":{"tax":0,"penalty":0,"interest":0,"discounts":0,"totalToPay":0},"pendingPeriods":[],"validations":[],"warnings":[]}}
        """;

    public const string ConDeuda = """
        {"id":"ext-1","status":"succeeded","result":{"status":"con_deuda","totals":{"tax":300000,"penalty":20000,"interest":15000,"discounts":0,"totalToPay":335000},
         "pendingPeriods":[{"period":2025,"tax":300000,"penalty":20000,"interest":15000,"discounts":0,"totalToPay":335000,"appraisal":25000000,"dueDate":"2025-07-31","fileId":"archivo-2025","paymentReference":"REF-2025-1","otherCharges":[],"previousPayments":0}],
         "validations":[{"field":"vehicleServiceType","sent":"X","portal":"Y","result":"mismatch"}],"warnings":[{"code":"periodo_en_cobro","message":"texto del proveedor","period":2025}],
         "consolidatedFileId":"archivo-consolidado"}}
        """;
}

/// <summary>Credenciales en cola; sin cola devuelve la última. <see cref="Lanzar"/> simula Identidad caída.</summary>
internal sealed class FuenteCredencialFalsa : IFuenteCredencialImpuesto
{
    public const string Llave = "llave-sintetica-0001";

    public Queue<CredencialImpuesto> Respuestas { get; } = new();

    public CredencialImpuesto Ultima { get; set; } = new(true, Llave, 1, MotivoSinCredencial.Ninguno);

    public FalloCredencialImpuestoException? Lanzar { get; set; }

    public List<(Guid Tenant, bool Forzar)> Pedidos { get; } = [];

    public Task<CredencialImpuesto> ObtenerAsync(Guid tenantId, bool forzar, CancellationToken ct)
    {
        Pedidos.Add((tenantId, forzar));
        if (Lanzar is { } ex)
            throw ex;
        if (Respuestas.Count > 0)
            Ultima = Respuestas.Dequeue();
        return Task.FromResult(Ultima);
    }

    public static CredencialImpuesto SinLlave() => new(false, null, 0, MotivoSinCredencial.SinConfigurar);
}

internal sealed class SeguimientoEnMemoria : ISeguimientoImpuesto
{
    private readonly ConcurrentDictionary<Guid, SeguimientoImpuesto> _filas = new();

    public List<string> Operaciones { get; } = [];

    public void Sembrar(SeguimientoImpuesto fila) => _filas[fila.TrabajoId] = fila;

    public SeguimientoImpuesto? Fila(Guid trabajoId) => _filas.TryGetValue(trabajoId, out var f) ? f : null;

    public Task<SeguimientoImpuesto?> LeerAsync(Guid trabajoId, CancellationToken ct) => Task.FromResult(Fila(trabajoId));

    public Task<SeguimientoImpuesto> ReservarAsync(Guid trabajoId, Guid tenantId, string departamento, CancellationToken ct)
    {
        Operaciones.Add("reservar");
        return Task.FromResult(_filas.GetOrAdd(trabajoId, id => new SeguimientoImpuesto(id, null, null, "reserved")));
    }

    public Task GuardarIdExternoAsync(Guid trabajoId, string idExterno, DateTimeOffset enviadoEn, CancellationToken ct)
    {
        Operaciones.Add("guardar");
        _filas[trabajoId] = new SeguimientoImpuesto(trabajoId, idExterno, enviadoEn, "queued");
        return Task.CompletedTask;
    }

    public Task AnotarSondeoAsync(Guid trabajoId, string estado, CancellationToken ct)
    {
        Operaciones.Add("sondeo:" + estado);
        return Task.CompletedTask;
    }

    public Task OlvidarIdExternoAsync(Guid trabajoId, CancellationToken ct)
    {
        Operaciones.Add("olvidar");
        _filas[trabajoId] = new SeguimientoImpuesto(trabajoId, null, null, "reserved");
        return Task.CompletedTask;
    }

    public Task CerrarAsync(Guid trabajoId, string estado, CancellationToken ct)
    {
        Operaciones.Add("cerrar:" + estado);
        var actual = Fila(trabajoId);
        _filas[trabajoId] = new SeguimientoImpuesto(trabajoId, actual?.IdExterno, actual?.EnviadoEn, estado);
        return Task.CompletedTask;
    }
}

/// <summary>Reloj que avanza solo lo que se espera: <c>Task.Delay(x, reloj)</c> mueve la hora x y vuelve sin esperar de verdad.</summary>
internal sealed class RelojFalso(DateTimeOffset inicio) : TimeProvider
{
    private readonly object _candado = new();
    private DateTimeOffset _ahora = inicio;

    public RelojFalso()
        : this(new DateTimeOffset(2026, 11, 19, 15, 0, 0, TimeSpan.Zero))
    {
    }

    public List<TimeSpan> Esperas { get; } = [];

    public override DateTimeOffset GetUtcNow()
    {
        lock (_candado)
            return _ahora;
    }

    public void Avanzar(TimeSpan cuanto)
    {
        lock (_candado)
            _ahora += cuanto;
    }

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        if (dueTime == Timeout.InfiniteTimeSpan)
            return new TimerNulo();
        lock (_candado)
        {
            _ahora += dueTime;
            Esperas.Add(dueTime);
        }

        ThreadPool.QueueUserWorkItem(_ => callback(state));
        return new TimerNulo();
    }

    private sealed class TimerNulo : ITimer
    {
        public bool Change(TimeSpan dueTime, TimeSpan period) => true;

        public void Dispose()
        {
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

internal sealed class ListaLogger<T> : ILogger<T>
{
    public List<string> Textos { get; } = [];

    public List<EventId> Eventos { get; } = [];

    public List<LogLevel> Niveles { get; } = [];

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        Eventos.Add(eventId);
        Niveles.Add(logLevel);
        Textos.Add(formatter(state, exception));
        if (state is IEnumerable<KeyValuePair<string, object?>> pares)
        {
            foreach (var par in pares)
                Textos.Add($"{par.Key}={par.Value}");
        }

        if (exception is not null)
            Textos.Add(exception.ToString());
    }
}
