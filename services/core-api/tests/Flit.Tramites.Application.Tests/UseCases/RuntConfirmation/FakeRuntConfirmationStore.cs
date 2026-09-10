using Flit.Tramites.Application.UseCases.RuntConfirmation;
using Flit.Tramites.Domain.RuntConfirmation;

namespace Flit.Tramites.Application.Tests.UseCases.RuntConfirmation;

/// <summary>Doble en memoria del almacén de Confirmación RUNT: candidatos, crudos, corridas e intentos.</summary>
internal sealed class FakeRuntConfirmationStore : IRuntConfirmationStore
{
    public List<RuntConfirmationCandidate> Candidates { get; } = [];
    public Dictionary<Guid, string> Payloads { get; } = [];
    public Dictionary<Guid, string> Baselines { get; } = [];
    public List<RuntConfirmationRun> Runs { get; } = [];
    public List<RuntConfirmationAttempt> Attempts { get; } = [];
    public List<(Guid InstanceId, RuntConfirmationInstanceUpdate Update)> Updates { get; } = [];
    public bool RunInProgress { get; set; }

    public Task<IReadOnlyList<RuntConfirmationCandidate>> ListUniverseAsync(RuntConfirmationUniverseFilter filter, CancellationToken ct = default)
    {
        IReadOnlyList<RuntConfirmationCandidate> list = Candidates
            .Where(c => c.RuntConfirmedAt is null
                && c.RuntAttempts < filter.MaxAttempts
                && c.RuntFlag is not (RuntConfirmationFlags.NoVerificable or RuntConfirmationFlags.Tope)
                && !RuntProcedureEquivalence.ExcludedProcedureTypeCodes.Contains(c.ProcedureTypeCode)
                && (c.ApprovedAt ?? c.CreatedAt).AddDays(filter.GraceDays) <= filter.NowUtc)
            .ToList();
        return Task.FromResult(list);
    }

    public Task<RuntConfirmationCandidate?> GetCandidateAsync(Guid instanceId, CancellationToken ct = default) =>
        Task.FromResult(Candidates.FirstOrDefault(c => c.InstanceId == instanceId));

    public Task<Guid?> SaveRawPayloadAsync(Guid tenantId, Guid instanceId, string providerKey, string? subjectKey, string rawJson, DateTimeOffset queriedAt, CancellationToken ct = default)
    {
        var id = Guid.CreateVersion7();
        lock (_gate)
        {
            Payloads[id] = rawJson;
        }
        return Task.FromResult<Guid?>(id);
    }

    public Task<string?> GetPayloadJsonAsync(Guid payloadId, CancellationToken ct = default) =>
        Task.FromResult(Payloads.TryGetValue(payloadId, out var json) ? json : null);

    public Task<string?> GetBaselinePayloadJsonAsync(Guid instanceId, DateTimeOffset cutoffAt, CancellationToken ct = default) =>
        Task.FromResult(Baselines.TryGetValue(instanceId, out var json) ? json : null);

    public Task<RuntConfirmationRun> StartRunAsync(RuntConfirmationRun run, CancellationToken ct = default)
    {
        if (run.Id == Guid.Empty) run.Id = Guid.CreateVersion7();
        Runs.Add(run);
        return Task.FromResult(run);
    }

    public Task FinishRunAsync(RuntConfirmationRun run, CancellationToken ct = default) => Task.CompletedTask;

    public Task<bool> IsRunInProgressAsync(TimeSpan staleAfter, CancellationToken ct = default) => Task.FromResult(RunInProgress);

    public Task<DateTimeOffset?> GetLastScheduledRunStartedAtAsync(bool includeSkipped, CancellationToken ct = default) =>
        Task.FromResult(Runs.Where(r => r.Trigger == RuntConfirmationRunTriggers.Scheduled && (includeSkipped || r.SkippedReason == null)).Select(r => (DateTimeOffset?)r.StartedAt).Max());

    private readonly Lock _gate = new();

    // La corrida graba desde varias tareas a la vez (concurrencia acotada): el doble debe ser
    // seguro para hilos igual que el almacén real (scope propio por operación).
    /// <summary>Simula que la BD rechaza el intento (p. ej. un CHECK): la primera grabación que cumpla el predicado revienta.</summary>
    public Func<RuntConfirmationAttempt, bool>? RejectRecordOnce { get; set; }

    public Task RecordAttemptAsync(RuntConfirmationAttempt attempt, RuntConfirmationInstanceUpdate update, CancellationToken ct = default)
    {
        lock (_gate)
        {
            if (RejectRecordOnce is { } reject && reject(attempt))
            {
                RejectRecordOnce = null;
                throw new InvalidOperationException("23514: new row violates check constraint \"ck_x\"");
            }

            Attempts.Add(attempt);
            Updates.Add((attempt.ProcedureInstanceId, update));

            var i = Candidates.FindIndex(c => c.InstanceId == attempt.ProcedureInstanceId);
            if (i >= 0)
            {
                var c = Candidates[i];
                Candidates[i] = c with
                {
                    RuntAttempts = update.IncrementAttempts ? c.RuntAttempts + 1 : c.RuntAttempts,
                    RuntConfirmedAt = update.ConfirmedAt ?? c.RuntConfirmedAt,
                    RuntFlag = update.ClearFlag ? null : update.Flag ?? c.RuntFlag,
                };
            }
        }

        return Task.CompletedTask;
    }

    public Task<RuntConfirmationAttempt?> GetAttemptAsync(Guid attemptId, CancellationToken ct = default) =>
        Task.FromResult(Attempts.FirstOrDefault(a => a.Id == attemptId));

    public Task<RuntConfirmationAttempt?> GetLatestAttemptForRunAsync(Guid runId, Guid instanceId, CancellationToken ct = default) =>
        Task.FromResult(Attempts.LastOrDefault(a => a.RunId == runId && a.ProcedureInstanceId == instanceId));

    public Dictionary<Guid, string> Statuses { get; } = [];

    public Task<string?> GetProcedureStatusAsync(Guid instanceId, CancellationToken ct = default) =>
        Task.FromResult(Statuses.TryGetValue(instanceId, out var s) ? s : Candidates.Any(c => c.InstanceId == instanceId) ? "aprobado" : null);
}
