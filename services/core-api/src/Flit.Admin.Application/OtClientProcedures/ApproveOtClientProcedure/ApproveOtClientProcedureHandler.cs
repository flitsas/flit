using Flit.Admin.Domain.OtClientProcedures;

namespace Flit.Admin.Application.OtClientProcedures.ApproveOtClientProcedure;

/// <summary>Aprueba un trámite pending_ot de un cliente OT (HU #10217 AC2).</summary>
public sealed class ApproveOtClientProcedureHandler
{
    private const string PendingOt = "pending_ot";

    private readonly IOtClientProcedureRepository _repository;

    public ApproveOtClientProcedureHandler(IOtClientProcedureRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    public async Task<ApproveOtClientProcedureResult> HandleAsync(
        ApproveOtClientProcedureCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

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
            cancellationToken).ConfigureAwait(false);

        return updated is null
            ? ApproveOtClientProcedureResult.InvalidState()
            : ApproveOtClientProcedureResult.Approved(OtClientProcedureMapper.ToResponse(updated));
    }
}
