using Flit.Admin.Domain.OtClientProcedures;

namespace Flit.Admin.Application.OtClientProcedures.RejectOtClientProcedure;

/// <summary>Rechaza un trámite pending_ot con motivo obligatorio (HU #10217 AC3).</summary>
public sealed class RejectOtClientProcedureHandler
{
    private const string PendingOt = "pending_ot";

    private readonly IOtClientProcedureRepository _repository;

    public RejectOtClientProcedureHandler(IOtClientProcedureRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    public async Task<RejectOtClientProcedureResult> HandleAsync(
        RejectOtClientProcedureCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(command.Request);

        if (string.IsNullOrWhiteSpace(command.Request.Reason))
        {
            return RejectOtClientProcedureResult.ValidationFailed(
                new FieldError("reason", "REASON_REQUIRED"));
        }

        var existing = await _repository
            .GetByIdAsync(command.OtTenantId, command.ProcedureInstanceId, cancellationToken)
            .ConfigureAwait(false);

        if (existing is null)
        {
            return RejectOtClientProcedureResult.NotFound();
        }

        if (!string.Equals(existing.Status, PendingOt, StringComparison.Ordinal))
        {
            return RejectOtClientProcedureResult.InvalidState();
        }

        var updated = await _repository.RejectAsync(
            command.OtTenantId,
            command.ProcedureInstanceId,
            command.Request.Reason.Trim(),
            command.RejectedBy,
            cancellationToken).ConfigureAwait(false);

        return updated is null
            ? RejectOtClientProcedureResult.InvalidState()
            : RejectOtClientProcedureResult.Rejected(OtClientProcedureMapper.ToResponse(updated));
    }
}
