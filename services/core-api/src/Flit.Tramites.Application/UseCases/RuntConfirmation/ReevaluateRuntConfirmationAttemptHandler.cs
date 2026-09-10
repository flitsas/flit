using Flit.Tramites.Domain.RuntConfirmation;

namespace Flit.Tramites.Application.UseCases.RuntConfirmation;

public sealed record ReevaluateRuntConfirmationAttemptCommand(Guid AttemptId, Guid? ActorUserId);

public sealed record ReevaluateRuntConfirmationAttemptResult(
    ReevaluateStatus Status,
    RuntConfirmationAttempt? NewAttempt);

public enum ReevaluateStatus
{
    Ok,
    AttemptNotFound,
    InstanceNotFound,
    NoRawPayload,
}

/// <summary>
/// Re-evalúa un intento desde su crudo guardado con la versión VIGENTE de la regla, sin llamar al
/// proveedor (HU #12308 AC12). Produce un intento NUEVO que apunta al original; el original no se
/// toca —es evidencia—. Si el veredicto nuevo es Confirmado, marca el trámite; no consume número de
/// intento porque no hubo consulta.
/// </summary>
public sealed class ReevaluateRuntConfirmationAttemptHandler(IRuntConfirmationStore store)
{
    public async Task<ReevaluateRuntConfirmationAttemptResult> HandleAsync(
        ReevaluateRuntConfirmationAttemptCommand command, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var original = await store.GetAttemptAsync(command.AttemptId, ct).ConfigureAwait(false);
        if (original is null)
            return new(ReevaluateStatus.AttemptNotFound, null);

        var candidate = await store.GetCandidateAsync(original.ProcedureInstanceId, ct).ConfigureAwait(false);
        if (candidate is null)
            return new(ReevaluateStatus.InstanceNotFound, null);

        if (original.RawPayloadId is null && original.SellerRawPayloadId is null)
            return new(ReevaluateStatus.NoRawPayload, null);

        var primary = original.RawPayloadId is Guid p ? await store.GetPayloadJsonAsync(p, ct).ConfigureAwait(false) : null;
        var seller = original.SellerRawPayloadId is Guid s ? await store.GetPayloadJsonAsync(s, ct).ConfigureAwait(false) : null;
        var baseline = await store.GetBaselinePayloadJsonAsync(candidate.InstanceId, candidate.CutoffAt, ct).ConfigureAwait(false);

        var decision = RuntConfirmationEvaluator.Evaluate(candidate, primary, seller, baseline);
        var now = DateTimeOffset.UtcNow;

        // Sin consulta no hay intento nuevo que contar: solo se propaga un Confirmado o una marca.
        var update = decision.Verdict switch
        {
            RuntConfirmationVerdict.Confirmed => new RuntConfirmationInstanceUpdate(false, now, null, ClearFlag: true),
            RuntConfirmationVerdict.Discrepancy => new RuntConfirmationInstanceUpdate(false, null, RuntConfirmationFlags.Discrepancia, false),
            RuntConfirmationVerdict.Unverifiable => new RuntConfirmationInstanceUpdate(false, null, RuntConfirmationFlags.NoVerificable, false),
            _ => new RuntConfirmationInstanceUpdate(false, null, null, false),
        };

        var attempt = new RuntConfirmationAttempt
        {
            Id = Guid.CreateVersion7(),
            TenantId = original.TenantId,
            ProcedureInstanceId = original.ProcedureInstanceId,
            RunId = null,
            AttemptNo = original.AttemptNo,
            QueriedAt = now,
            ProviderKey = original.ProviderKey,
            QueryKind = RuntConfirmationQueryKinds.Reevaluation,
            Verdict = RuntConfirmationVerdictCodes.ToCode(decision.Verdict),
            ReasonText = Truncate(decision.Reason, 2000),
            RuleVersion = decision.RuleVersion,
            RawPayloadId = original.RawPayloadId,
            SellerRawPayloadId = original.SellerRawPayloadId,
            RequestedBy = command.ActorUserId,
            ReevaluatedFromAttemptId = original.Id,
            FlagApplied = update.Flag,
            CreatedAt = now,
        };

        await store.RecordAttemptAsync(attempt, update, ct).ConfigureAwait(false);
        return new(ReevaluateStatus.Ok, attempt);
    }

    internal static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max];
}
