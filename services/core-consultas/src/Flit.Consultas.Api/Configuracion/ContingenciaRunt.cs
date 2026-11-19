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

/// <summary>
/// Lo que está forzado hoy por tipo, con caché corta para no ir a la base en cada consulta (HU #14647: ≤ 30 s). Se invalida al
/// guardar en este proceso; otra réplica lo ve al vencer la <see cref="Vigencia"/>. Si la base falla no rompe la consulta: sigue
/// con lo último que supo (o sin contingencia) y reintenta pronto.
/// </summary>
internal sealed partial class ContingenciaRuntVigente(IServiceScopeFactory scopes, TimeProvider time, ILogger<ContingenciaRuntVigente> logger) : IDisposable
{
    public static readonly TimeSpan Vigencia = TimeSpan.FromSeconds(20);

    private static readonly TimeSpan ReintentoTrasFallo = TimeSpan.FromSeconds(5);

    private readonly SemaphoreSlim _carga = new(1, 1);
    private volatile Instantanea _actual = new(DateTimeOffset.MinValue, new Dictionary<string, string>());

    private sealed record Instantanea(DateTimeOffset Vence, IReadOnlyDictionary<string, string> Forzados);

    /// <summary>El proveedor forzado para el <paramref name="tipo"/>, o nulo si no hay contingencia.</summary>
    public async Task<string?> ForzadoAsync(string tipo, CancellationToken ct)
    {
        var instantanea = _actual;
        if (instantanea.Vence <= time.GetUtcNow())
            instantanea = await RecargarAsync(ct).ConfigureAwait(false);

        return instantanea.Forzados.TryGetValue(tipo, out var proveedor) ? proveedor : null;
    }

    public void Dispose() => _carga.Dispose();

    [LoggerMessage(EventId = 1, Level = LogLevel.Warning, Message = "No se pudo leer la contingencia de proveedor RUNT; se sigue con la última conocida.")]
    private partial void LecturaFallida(Exception ex);

    /// <summary>Descarta lo guardado: la siguiente consulta lee la base.</summary>
    public void Invalidar() => _actual = _actual with { Vence = DateTimeOffset.MinValue };

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
                await using var scope = scopes.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<ConsultasDb>();
                var forzados = await db.ContingenciasRunt.AsNoTracking()
                    .Where(c => c.Provider != null)
                    .ToDictionaryAsync(c => c.Kind, c => c.Provider!, ct).ConfigureAwait(false);
                _actual = new Instantanea(ahora + Vigencia, forzados);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LecturaFallida(ex);
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
