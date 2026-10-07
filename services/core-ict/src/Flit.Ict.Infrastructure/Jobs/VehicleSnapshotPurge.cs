using Flit.Ict.Domain.Abstractions;
using Microsoft.Extensions.Logging;

namespace Flit.Ict.Infrastructure.Jobs;

/// <summary>
/// Bug #13304 (H-1) — purga best-effort del resultado completo de la consulta RUNT
/// (<c>ict.external_integration_source_response.vehicle_snapshot</c>, @pii:high) en cada camino que deja el
/// master fuera del envío: borrador creado (ps=5), novedad del envío o del orquestador (ps=4) y anulación
/// (ps=6). La purga va DESPUÉS de los registros del estado: si falla, el estado, el histórico y el webhook ya
/// quedaron; el fallo se registra sin PII y el barrido de <see cref="RetentionJob"/> la recoge después.
/// </summary>
internal static partial class VehicleSnapshotPurge
{
    internal const string CaminoBorrador = "borrador";

    internal const string CaminoNovedadEnvio = "novedad_envio";

    internal const string CaminoNovedadOrquestador = "novedad_orquestador";

    internal const string CaminoAnulado = "anulado";

    /// <summary>Purga el snapshot del master. Nunca lanza salvo cancelación: devuelve false si falló.</summary>
    internal static async Task<bool> BestEffortAsync(
        Func<CancellationToken, Task<int>> purgar, Guid masterId, string camino, ILogger logger, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(purgar);
        try
        {
            await purgar(ct);
            return true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
#pragma warning disable CA1031 // best-effort: una purga fallida no debe romper el cambio de estado ya registrado
        catch (Exception ex)
        {
            // Solo el tipo: el mensaje de una excepción de BD puede citar valores de la fila (PII).
            Log.PurgeFailed(logger, camino, masterId, ex.GetType().Name);
            return false;
        }
#pragma warning restore CA1031
    }

    /// <summary>Atajo con el lector del snapshot del scope del job.</summary>
    internal static Task<bool> BestEffortAsync(
        IIctVehicleSnapshotReader snapshots, Guid masterId, string camino, ILogger logger, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(snapshots);
        return BestEffortAsync(c => snapshots.PurgeAsync(masterId, c), masterId, camino, logger, ct);
    }

    /// <summary>
    /// Ejecuta los registros del estado y luego la purga best-effort. Si los registros fallan la excepción
    /// sigue su curso y no se purga (el barrido por antigüedad lo hará).
    /// </summary>
    internal static async Task DespuesDeAsync(
        Func<CancellationToken, Task> registrar,
        Func<CancellationToken, Task<int>> purgar,
        Guid masterId,
        string camino,
        ILogger logger,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(registrar);
        await registrar(ct);
        await BestEffortAsync(purgar, masterId, camino, logger, ct);
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Warning,
            Message = "ICT snapshot RUNT: no se pudo purgar (camino {Camino}, master {MasterId}, {ErrorType}); lo vacía el barrido de retención.")]
        public static partial void PurgeFailed(ILogger logger, string camino, Guid masterId, string errorType);
    }
}
