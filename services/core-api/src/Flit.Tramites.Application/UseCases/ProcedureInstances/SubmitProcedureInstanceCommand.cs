using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Enums;
using Flit.Tramites.Domain.Integration;
using Flit.Tramites.Domain.Repositories;

namespace Flit.Tramites.Application.UseCases.ProcedureInstances;

public sealed class SubmitProcedureInstanceHandler(
    IProcedureInstanceRepository repo,
    IProcedureTypeRepository typeRepo,
    IProcedureStateChangeNotifier stateChangeNotifier,
    IOtRuleGate otRuleGate)
{
    public async Task<(ProcedureInstanceSummary? Result, string? Error)> HandleAsync(
        Guid id,
        Guid tenantId,
        CancellationToken ct = default)
    {
        var instance = await repo.GetByIdWithWizardGraphAsync(id, tenantId, ct);
        if (instance is null)
            return (null, "not_found");

        if (instance.Status != ProcedureInstanceStatus.Draft)
            return (null, "not_draft");

        var procedureType = await typeRepo.GetByIdAsync(instance.ProcedureTypeId, ct);
        if (procedureType is null || procedureType.PublicationStatus != PublicationStatus.Published)
            return (null, "not_published");

        var gateErrors = SubmitGate.Evaluate(instance);
        if (gateErrors.Count > 0)
            return (null, gateErrors[0]);

        var ruleResult = await otRuleGate.EvaluateSubmissionAsync(
            instance.TransitOfficeId,
            instance.ProcedureTypeId,
            procedureType.Code,
            ct).ConfigureAwait(false);

        if (ruleResult.IsBlocked)
        {
            return (null, ruleResult.ErrorCode ?? "ot_rule_blocked");
        }

        var now = DateTimeOffset.UtcNow;
        instance.Status = ProcedureInstanceStatus.Submitted;
        instance.SubmittedAt = now;
        instance.UpdatedAt = now;

        var statusHistory = new ProcedureInstanceStatusHistory
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ProcedureInstanceId = id,
            FromStatus = ProcedureInstanceStatus.Draft,
            ToStatus = ProcedureInstanceStatus.Submitted,
            ChangedAt = now
        };
        instance.StatusHistory.Add(statusHistory);
        repo.Add(statusHistory);

        await repo.SaveChangesAsync(ct);

        await stateChangeNotifier.NotifyAsync(
            new ProcedureStateChangeEvent(
                tenantId,
                id,
                ProcedureInstanceStatus.Draft,
                ProcedureInstanceStatus.Submitted,
                now),
            ct).ConfigureAwait(false);

        return (CreateProcedureInstanceHandler.ToSummary(instance), null);
    }
}
