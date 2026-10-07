using System.Diagnostics;
using Flit.Api.Identity;
using Flit.Consultas.Api.Persistence;
using Flit.Consultas.Grpc.V1;
using Flit.Platform.Sdk.Grpc;
using Grpc.Core;

namespace Flit.Consultas.Api.Grpc;

/// <summary>
/// Mide cada consulta (HU #13345): una fila en <c>consultas.consumo</c> al terminar, salga bien o mal. Si guardar la
/// medición falla, la consulta no falla: se registra en el log y se devuelve la respuesta.
/// </summary>
internal sealed class ConsumoRecorder(ConsultasDb db, TimeProvider time, ILogger<ConsumoRecorder> logger)
{
    public async Task<T> MedirAsync<T>(ServerCallContext context, string fuente, Func<Task<T>> consulta, Func<T, (string Proveedor, string Resultado, bool DesdeCache)> resumen)
    {
        ArgumentNullException.ThrowIfNull(consulta);
        ArgumentNullException.ThrowIfNull(resumen);
        var caller = PlatformServiceCaller.From(context);
        var reloj = Stopwatch.StartNew();
        try
        {
            var respuesta = await consulta().ConfigureAwait(false);
            var (proveedor, resultado, desdeCache) = resumen(respuesta);
            await GuardarAsync(caller, fuente, proveedor, resultado, desdeCache, reloj.Elapsed).ConfigureAwait(false);
            return respuesta;
        }
        catch (Exception) when (!context.CancellationToken.IsCancellationRequested)
        {
            await GuardarAsync(caller, fuente, string.Empty, "error", false, reloj.Elapsed).ConfigureAwait(false);
            throw;
        }
    }

    public static string Resultado(Semaforo semaforo) => semaforo switch
    {
        Semaforo.Verde => "verde",
        Semaforo.Amarillo => "amarillo",
        Semaforo.Rojo => "rojo",
        _ => "sin_semaforo",
    };

    /// <summary><c>svc-tramites</c> → <c>tramites</c>.</summary>
    internal static string Producto(string clientId) =>
        clientId.StartsWith(OidcDefaults.ServiceClientPrefix, StringComparison.Ordinal) ? clientId[OidcDefaults.ServiceClientPrefix.Length..] : clientId;

    private async Task GuardarAsync(PlatformServiceCaller caller, string fuente, string proveedor, string resultado, bool desdeCache, TimeSpan latencia)
    {
        try
        {
            db.ChangeTracker.Clear();
            db.Consumos.Add(new ConsumoConsulta
            {
                Id = Guid.CreateVersion7(),
                TenantId = caller.TenantId,
                Producto = Producto(caller.ClientId),
                Fuente = fuente,
                Proveedor = proveedor,
                Resultado = resultado,
                DesdeCache = desdeCache,
                LatenciaMs = (int)Math.Min(int.MaxValue, latencia.TotalMilliseconds),
                OcurridoEn = time.GetUtcNow(),
            });
            await db.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
        }
#pragma warning disable CA1031 // La medición nunca tumba la consulta.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            ConsumoLog.NoGuardado(logger, fuente, ex);
        }
    }
}

internal static partial class ConsumoLog
{
    [LoggerMessage(EventId = 7401, Level = LogLevel.Error, Message = "No se pudo guardar el consumo de una consulta de {Fuente}")]
    public static partial void NoGuardado(ILogger logger, string fuente, Exception ex);
}
