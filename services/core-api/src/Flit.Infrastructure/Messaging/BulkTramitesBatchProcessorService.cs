using Flit.Tramites.Application.BulkTramites.Processing;
using Flit.Tramites.Domain.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Flit.Infrastructure.Messaging;

/// <summary>
/// Worker que vacía la cola de lotes de carga masiva (HU #12523). Es lo que hace que la carga sea
/// NO BLOQUEANTE: el endpoint de HU #12522 solo persiste el lote en <c>queued</c> y responde; el
/// usuario sigue usando la aplicación mientras este servicio consulta los proveedores externos, que
/// es la parte lenta (una fila puede tardar segundos).
///
/// <para>Un lote a la vez y sus filas en secuencia: el cuello de botella es el proveedor externo, y
/// ya se sabe que consultas simultáneas de la misma placa se colapsan (HU #12309).</para>
/// </summary>
internal sealed class BulkTramitesBatchProcessorService(
    IServiceScopeFactory scopeFactory,
    ILogger<BulkTramitesBatchProcessorService> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(5);
    private const int MaxBatchesPerCycle = 5;

    /// <summary>
    /// Un lote «en proceso» sin ninguna fila resuelta en este tiempo se considera huérfano (el proceso
    /// murió o el guardado falló a mitad de lote) y se reclama. Holgado a propósito: una fila puede
    /// tardar medio minuto entre consultas al proveedor y el reintento con espera.
    /// </summary>
    private static readonly TimeSpan ReclaimAfter = TimeSpan.FromMinutes(10);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(StartupDelay, stoppingToken).ConfigureAwait(false); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessQueuedAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
#pragma warning disable CA1031 // Un ciclo fallido no puede matar al worker: se reintenta al siguiente.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                BulkTramitesProcessorLog.CycleError(logger, ex);
            }

            try { await Task.Delay(PollInterval, stoppingToken).ConfigureAwait(false); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task ProcessQueuedAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IBulkTramitesBatchRepository>();

        var pendientes = await repository
            .ListQueuedIdsAsync(MaxBatchesPerCycle, DateTimeOffset.UtcNow - ReclaimAfter, ct)
            .ConfigureAwait(false);
        if (pendientes.Count == 0)
        {
            return;
        }

        foreach (var batchId in pendientes)
        {
            // Un scope por lote: el procesador arrastra los casos de uso del wizard, que son scoped
            // y mantienen su propio DbContext con el grafo de la instancia cargado.
            using var batchScope = scopeFactory.CreateScope();
            var processor = batchScope.ServiceProvider.GetRequiredService<BulkTramitesBatchProcessor>();

            await processor.ProcessAsync(batchId, ct).ConfigureAwait(false);
        }
    }
}

internal static partial class BulkTramitesProcessorLog
{
    [LoggerMessage(Level = LogLevel.Error,
        Message = "Carga masiva de trámites: falló el ciclo de procesamiento de lotes; se reintentará.")]
    public static partial void CycleError(ILogger logger, Exception ex);
}
