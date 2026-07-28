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
    ILogger<BusinessValidationJob> logger) : IctPollingJob(scopeFactory, options, logger)
{
    protected override TimeSpan PollInterval => TimeSpan.FromSeconds(Options.BusinessPollSeconds);

    protected override string JobName => "business-validation";

    protected override Task RunCycleAsync(IServiceScope scope, CancellationToken ct) =>
        // Advisory lock: con >1 réplica, solo una corre el SP por ciclo (evita doble emisión de estados/webhooks).
        RunUnderAdvisoryLockAsync(scope, IctAdvisoryLock.Keys.Business, async connection =>
        {
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = "CALL ict.sp_processor_validation_business()";
            await cmd.ExecuteNonQueryAsync(ct);
        }, ct);
}

/// <summary>Job 2 (v1 RunExternalApiValidations): ejecuta el SP que identifica las fuentes externas.</summary>
public sealed class ExternalValidationJob(
    IServiceScopeFactory scopeFactory,
    IOptions<IctJobOptions> options,
    ILogger<ExternalValidationJob> logger) : IctPollingJob(scopeFactory, options, logger)
{
    protected override TimeSpan PollInterval => TimeSpan.FromSeconds(Options.ExternalPollSeconds);

    protected override string JobName => "external-validation";

    protected override Task RunCycleAsync(IServiceScope scope, CancellationToken ct) =>
        RunUnderAdvisoryLockAsync(scope, IctAdvisoryLock.Keys.External, async connection =>
        {
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = "CALL ict.sp_processor_validation_external()";
            await cmd.ExecuteNonQueryAsync(ct);
        }, ct);
}
