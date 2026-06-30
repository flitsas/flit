using Flit.Admin.Domain.OtClientProcedures;
using Flit.Admin.Domain.OtProfile;

namespace Flit.Admin.Application.OtClientProcedures.ApproveOtClientProcedure;

/// <summary>Aprueba un trámite pending_ot de un cliente OT (HU #10217 AC2).</summary>
public sealed class ApproveOtClientProcedureHandler
{
    private const string PendingOt = "pending_ot";

    private readonly IOtClientProcedureRepository _repository;
    private readonly IQuipuxReadOnlyGuard _quipuxReadOnlyGuard;

    public ApproveOtClientProcedureHandler(
        IOtClientProcedureRepository repository,
        IQuipuxReadOnlyGuard quipuxReadOnlyGuard)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _quipuxReadOnlyGuard = quipuxReadOnlyGuard
            ?? throw new ArgumentNullException(nameof(quipuxReadOnlyGuard));
    }

    public async Task<ApproveOtClientProcedureResult> HandleAsync(
        ApproveOtClientProcedureCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var guardResult = await _quipuxReadOnlyGuard
            .ValidateActionAsync(command.OtTenantId, "aprobar", cancellationToken)
            .ConfigureAwait(false);
        if (!guardResult.IsAllowed)
        {
            return ApproveOtClientProcedureResult.QuipuxReadOnly();
        }

        var existing = await _repository
            .GetByIdAsync(command.OtTenantId, command.ProcedureInstanceId, cancellationToken)
            .ConfigureAwait(false);

        if (existing is null)
        {
            return ApproveOtClientProcedureResult.NotFound();
        }

        if (!string.Equals(existing.Status, PendingOt, StringComparison.Ordinal))
        {
            return ApproveOtClientProcedureResult.InvalidState();
        }

        var updated = await _repository.ApproveAsync(
            command.OtTenantId,
            command.ProcedureInstanceId,
            command.ApprovedBy,
            OtTransitionSource.OtAdmin,
            cancellationToken).ConfigureAwait(false);

        return updated is null
            ? ApproveOtClientProcedureResult.InvalidState()
            : ApproveOtClientProcedureResult.Approved(OtClientProcedureMapper.ToResponse(updated));
    }
}
