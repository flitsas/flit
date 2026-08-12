using Flit.Ict.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Flit.Ict.Infrastructure.Jobs;

/// <summary>Job 1 (v1 RunBusinessValidations): ejecuta el SP de validación de reglas de negocio.</summary>
public sealed class BusinessValidationJob(
    IServiceScopeFactory scopeFactory,
    IOptions<IctJobOptions> options,
    IIctJobSettingsProvider settings,
    ILogger<BusinessValidationJob> logger) : IctPollingJob(scopeFactory, options, settings, logger)
{
    protected override TimeSpan PollInterval => TimeSpan.FromSeconds(JobSettings.BusinessPollSeconds);

    protected override string JobName => "business-validation";

    protected override Task RunCycleAsync(IServiceScope scope, CancellationToken ct) =>
        // Advisory lock: con >1 réplica, solo una corre el SP por ciclo (evita doble emisión de estados/webhooks).
        RunUnderAdvisoryLockAsync(scope, IctAdvisoryLock.Keys.Business, async connection =>
        {
            // El SP procesa UN lote (business_batch_size) por CALL y retorna — cada CALL es su propia
            // transacción (autocommit). Se re-invoca en bucle hasta drenar el backlog, eliminando la
            // transacción única gigante SIN usar COMMIT dentro del SP (no permitido en su contexto).
            for (var i = 0; i < MaxBatchesPerCycle && !ct.IsCancellationRequested; i++)
            {
                await using (var cmd = connection.CreateCommand())
                {
                    cmd.CommandText = "CALL ict.sp_processor_validation_business()";
                    await cmd.ExecuteNonQueryAsync(ct);
                }

                await using var remaining = connection.CreateCommand();
                // Solo importa si QUEDA algo pendiente (no cuántos): EXISTS corta al primer match en vez de
                // contar todo el backlog en cada iteración.
                remaining.CommandText =
                    "SELECT EXISTS(SELECT 1 FROM ict.external_integration_master " +
                    "WHERE business_validation = 0 AND process_status_id = 1 AND deleted_at IS NULL)";
                if (!(bool)(await remaining.ExecuteScalarAsync(ct))!)
                {
                    break; // backlog drenado
                }
            }
        }, ct);
}

/// <summary>Job 2 (v1 RunExternalApiValidations): ejecuta el SP que identifica las fuentes externas.</summary>
public sealed class ExternalValidationJob(
    IServiceScopeFactory scopeFactory,
    IOptions<IctJobOptions> options,
    IIctJobSettingsProvider settings,
    ILogger<ExternalValidationJob> logger) : IctPollingJob(scopeFactory, options, settings, logger)
{
    protected override TimeSpan PollInterval => TimeSpan.FromSeconds(JobSettings.ExternalPollSeconds);

    protected override string JobName => "external-validation";

    protected override Task RunCycleAsync(IServiceScope scope, CancellationToken ct) =>
        RunUnderAdvisoryLockAsync(scope, IctAdvisoryLock.Keys.External, async connection =>
        {
            // El SP procesa UN lote (external_batch_size) por CALL; se re-invoca en bucle hasta drenar,
            // igual que la validación de negocio (elimina la transacción única gigante sin COMMIT en el SP).
            for (var i = 0; i < MaxBatchesPerCycle && !ct.IsCancellationRequested; i++)
            {
                await using (var cmd = connection.CreateCommand())
                {
                    cmd.CommandText = "CALL ict.sp_processor_validation_external()";
                    await cmd.ExecuteNonQueryAsync(ct);
                }

                await using var remaining = connection.CreateCommand();
                // Solo importa si QUEDA algo pendiente (no cuántos): EXISTS corta al primer match.
                remaining.CommandText =
                    "SELECT EXISTS(SELECT 1 FROM ict.external_integration_master " +
                    "WHERE external_validation = 0 AND process_status_id = 2 AND business_validation = 2 " +
                    "AND deleted_at IS NULL)";
                if (!(bool)(await remaining.ExecuteScalarAsync(ct))!)
                {
                    break; // backlog drenado
                }
            }
        }, ct);
}
