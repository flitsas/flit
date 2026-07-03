using Flit.Admin.Domain.OtClientProcedures;
using Flit.Admin.Domain.OtProfile;

namespace Flit.Admin.Application.OtClientProcedures.RejectOtClientProcedure;

/// <summary>Rechaza un trámite entregado de un cliente OT (HU #10217 AC3 · N 03: entregado→rechazado).</summary>
public sealed class RejectOtClientProcedureHandler
{
    // N 03 (ADR-0022): el OT decide sobre trámites en estado 'entregado' (antes pending_ot).
    private const string EstadoEntregado = "entregado";

    private readonly IOtClientProcedureRepository _repository;
    private readonly IQuipuxReadOnlyGuard _quipuxReadOnlyGuard;

    public RejectOtClientProcedureHandler(
        IOtClientProcedureRepository repository,
        IQuipuxReadOnlyGuard quipuxReadOnlyGuard)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _quipuxReadOnlyGuard = quipuxReadOnlyGuard
            ?? throw new ArgumentNullException(nameof(quipuxReadOnlyGuard));
    }

    public async Task<RejectOtClientProcedureResult> HandleAsync(
        RejectOtClientProcedureCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (string.IsNullOrWhiteSpace(command.Request.Reason))
        {
            return RejectOtClientProcedureResult.ValidationFailed(
                new FieldError("reason", "El motivo de rechazo es obligatorio."));
        }

        var guardResult = await _quipuxReadOnlyGuard
            .ValidateActionAsync(command.OtTenantId, "rechazar", cancellationToken)
            .ConfigureAwait(false);
        if (!guardResult.IsAllowed)
        {
            return RejectOtClientProcedureResult.QuipuxReadOnly();
        }

        var existing = await _repository
            .GetByIdAsync(command.OtTenantId, command.ProcedureInstanceId, cancellationToken)
            .ConfigureAwait(false);

        if (existing is null)
        {
            return RejectOtClientProcedureResult.NotFound();
        }

        if (!string.Equals(existing.Status, EstadoEntregado, StringComparison.Ordinal))
        {
            return RejectOtClientProcedureResult.InvalidState();
        }

        var updated = await _repository.RejectAsync(
            command.OtTenantId,
            command.ProcedureInstanceId,
            command.Request.Reason.Trim(),
            command.RejectedBy,
            OtTransitionSource.OtAdmin,
            cancellationToken).ConfigureAwait(false);

        return updated is null
            ? RejectOtClientProcedureResult.InvalidState()
            : RejectOtClientProcedureResult.Rejected(OtClientProcedureMapper.ToResponse(updated));
    }
}
