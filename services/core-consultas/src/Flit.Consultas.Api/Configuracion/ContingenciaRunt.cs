using Flit.Consultas.Api.Persistence;
using Flit.Tramites.Application.UseCases.Consultations;
using Microsoft.EntityFrameworkCore;

namespace Flit.Consultas.Api.Configuracion;

/// <summary>
/// Reglas de la contingencia de proveedor RUNT (Épica #14644, HU #14647): qué tipos, qué proveedores atiende cada uno y a qué
/// llamadores aplica.
/// </summary>
internal static class ContingenciaRuntReglas
{
    /// <summary>Los tres tipos, en el orden en que se muestran.</summary>
    public static readonly IReadOnlyList<string> Tipos =
        [ConsultationKindKeys.VehicleVin, ConsultationKindKeys.VehiclePlate, ConsultationKindKeys.Conductor];

    private static readonly string[] ProveedoresVehiculo = ["kyverum_runt", "verifik"];
    private static readonly string[] ProveedoresConductor = ["kyverum_runt_conductor", "verifik_conductor"];

    /// <summary>
    /// Los productos a los que aplica: Trámites (core-api), Persona Natural e ICT (<c>PlatformServiceCaller.ClientId</c>).
    /// Diagnóstico (<c>svc-diagnostico</c>, también por el bus) y Comparendos conservan su cadena.
    /// </summary>
    private static readonly HashSet<string> Llamadores = new(["svc-tramites", "svc-personanatural", "svc-ict"], StringComparer.Ordinal);

    public static bool EsTipo(string? tipo) => tipo is not null && Tipos.Contains(tipo, StringComparer.Ordinal);

    /// <summary>Proveedores que puede forzar el <paramref name="tipo"/>; vacío si el tipo no existe.</summary>
    public static IReadOnlyList<string> ProveedoresDe(string? tipo) => tipo switch
    {
        ConsultationKindKeys.VehicleVin or ConsultationKindKeys.VehiclePlate => ProveedoresVehiculo,
        ConsultationKindKeys.Conductor => ProveedoresConductor,
        _ => [],
    };

    public static bool AplicaAlLlamador(string? clientId) => clientId is not null && Llamadores.Contains(clientId);
}


/// <summary>De dónde sale lo forzado: <c>tipo → proveedor</c> solo de los tipos con contingencia activa.</summary>
internal interface IFuenteContingenciaRunt
{
    Task<IReadOnlyDictionary<string, string>> LeerAsync(CancellationToken ct);
}

/// <summary>Lee <c>consultas.runt_contingencies</c> en un scope propio (el consumidor es un singleton).</summary>
internal sealed class FuenteContingenciaRuntDb(IServiceScopeFactory scopes) : IFuenteContingenciaRunt
{
    public async Task<IReadOnlyDictionary<string, string>> LeerAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ConsultasDb>();
        return await db.ContingenciasRunt.AsNoTracking()
            .Where(c => c.Provider != null)
            .ToDictionaryAsync(c => c.Kind, c => c.Provider!, ct).ConfigureAwait(false);
    }
}

/// <summary>
/// Lo que está forzado hoy por tipo, con caché corta para no ir a la base en cada consulta (HU #14647: ≤ 30 s).
/// <para>
/// Consistencia eventual: <see cref="Invalidar"/> (al guardar) solo alcanza a ESTA réplica; las demás ven el cambio al vencer la
/// <see cref="Vigencia"/>, de modo que entre réplicas la diferencia es de hasta 20 s. Una lectura que arrancó antes de un
/// <see cref="Invalidar"/> no repone el valor viejo: se descarta y se vuelve a leer.
/// </para>
/// <para>
/// Fail-open: si la base falla, la consulta NO se rompe; sigue con lo último que se supo (o sin contingencia), deja un warning y
/// la métrica <c>consultas.contingencia.lecturas_fallidas</c>, y reintenta a los <see cref="ReintentoTrasFallo"/>.
/// </para>
/// </summary>
internal sealed partial class ContingenciaRuntVigente(IFuenteContingenciaRunt fuente, TimeProvider time, ILogger<ContingenciaRuntVigente> logger) : IDisposable
{
    public static readonly TimeSpan Vigencia = TimeSpan.FromSeconds(20);

    public static readonly TimeSpan ReintentoTrasFallo = TimeSpan.FromSeconds(5);

    /// <summary>Máximo de relecturas seguidas si un guardado invalida mientras se lee.</summary>
    private const int MaximoLecturas = 3;

    internal const string MeterName = "Flit.Consultas";

    private static readonly System.Diagnostics.Metrics.Meter Meter = new(MeterName);

    private static readonly System.Diagnostics.Metrics.Counter<long> LecturasFallidas =
        Meter.CreateCounter<long>("consultas.contingencia.lecturas_fallidas", description: "Lecturas de la contingencia RUNT que fallaron (fail-open).");

    private static readonly System.Diagnostics.Metrics.Counter<long> Desvios =
        Meter.CreateCounter<long>("consultas.contingencia.desvios", description: "Consultas desviadas al proveedor de la contingencia RUNT.");

    private readonly SemaphoreSlim _carga = new(1, 1);
    private volatile Instantanea _actual = new(DateTimeOffset.MinValue, new Dictionary<string, string>());
    private int _version;

    private sealed record Instantanea(DateTimeOffset Vence, IReadOnlyDictionary<string, string> Forzados);

    /// <summary>El proveedor forzado para el <paramref name="tipo"/>, o nulo si no hay contingencia.</summary>
    public async Task<string?> ForzadoAsync(string tipo, CancellationToken ct)
    {
        var instantanea = _actual;
        if (instantanea.Vence <= time.GetUtcNow())
            instantanea = await RecargarAsync(ct).ConfigureAwait(false);

        return instantanea.Forzados.TryGetValue(tipo, out var proveedor) ? proveedor : null;
    }

    /// <summary>Descarta lo guardado: la siguiente consulta lee la base, y una lectura en vuelo no repone el valor viejo.</summary>
    public void Invalidar()
    {
        Interlocked.Increment(ref _version);
        _actual = _actual with { Vence = DateTimeOffset.MinValue };
    }

    /// <summary>Deja constancia (log de información y métrica) de que una consulta se desvió por la contingencia. Sin datos de la consulta.</summary>
    public void RegistrarDesvio(string tipo, string proveedorForzado, string clientId)
    {
        var producto = clientId.StartsWith("svc-", StringComparison.Ordinal) ? clientId["svc-".Length..] : clientId;
        Desvios.Add(1, new KeyValuePair<string, object?>("tipo", tipo), new KeyValuePair<string, object?>("proveedor", proveedorForzado),
            new KeyValuePair<string, object?>("producto", producto));
        ConsultaDesviada(tipo, proveedorForzado, producto);
    }

    public void Dispose() => _carga.Dispose();

    [LoggerMessage(EventId = 1, Level = LogLevel.Warning, Message = "No se pudo leer la contingencia de proveedor RUNT; se sigue con la última conocida.")]
    private partial void LecturaFallida(Exception ex);

    [LoggerMessage(EventId = 2, Level = LogLevel.Information, Message = "Consulta desviada por la contingencia RUNT: tipo={Tipo}, proveedor forzado={Proveedor}, producto={Producto}.")]
    private partial void ConsultaDesviada(string tipo, string proveedor, string producto);

    private async Task<Instantanea> RecargarAsync(CancellationToken ct)
    {
        await _carga.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var ahora = time.GetUtcNow();
            if (_actual.Vence > ahora)
                return _actual;

            try
            {
                for (var intento = 1; ; intento++)
                {
                    var version = Volatile.Read(ref _version);
                    var forzados = await fuente.LeerAsync(ct).ConfigureAwait(false);
                    if (version == Volatile.Read(ref _version))
                    {
                        _actual = new Instantanea(time.GetUtcNow() + Vigencia, forzados);
                        break;
                    }

                    // Hubo un guardado mientras se leía: lo leído puede ser el valor viejo. No se guarda como vigente.
                    if (intento == MaximoLecturas)
                    {
                        _actual = new Instantanea(DateTimeOffset.MinValue, forzados);
                        break;
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LecturaFallida(ex);
                LecturasFallidas.Add(1);
                _actual = _actual with { Vence = ahora + ReintentoTrasFallo };
            }

            return _actual;
        }
        finally
        {
            _carga.Release();
        }
    }
}
