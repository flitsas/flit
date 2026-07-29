using Flit.Admin.Domain.OtClientProcedures;
using Flit.Admin.Domain.OtProfile;

namespace Flit.Admin.Application.OtClientProcedures.RejectOtClientProcedure;

/// <summary>
/// Rechaza u observa un trámite entregado de un cliente OT (HU #10217 AC3 · N 03: entregado→rechazado).
/// HU #10871 (AC1) — cuando la request trae un checklist de ítems subsanables, la decisión se
/// registra como OBSERVACIÓN (entregado→subsanacion) en vez de rechazo definitivo: mismo endpoint,
/// mismo motivo obligatorio, distinto destino según haya o no ítems.
/// </summary>
public sealed class RejectOtClientProcedureHandler
{
    // N 03 (ADR-0022): el OT decide sobre trámites en estado 'entregado' (antes pending_ot). La ruta de
    // placa (Feature #10587 / HU #10785) NO cambia el status: el trámite siempre está 'entregado' cuando
    // el OT decide (el progreso de placa es un sub-estado interno).
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
            .GetByIdAsync(
                command.OtTenantId,
                command.ProcedureInstanceId,
                command.TransitOfficeId,
                cancellationToken)
            .ConfigureAwait(false);

        if (existing is null)
        {
            return RejectOtClientProcedureResult.NotFound();
        }

        if (!string.Equals(existing.Status, EstadoEntregado, StringComparison.Ordinal))
        {
            return RejectOtClientProcedureResult.InvalidState();
        }

        // Con ítems del checklist: observación (rechazado + metadata). Sin ítems: rechazo definitivo.
        var items = command.Request.Items?.Where(i => i is not null).ToList()
            ?? [];

        var updated = items.Count > 0
            ? await _repository.ObserveAsync(
                command.OtTenantId,
                command.ProcedureInstanceId,
                command.Request.Reason.Trim(),
                items,
                command.RejectedBy,
                OtTransitionSource.OtAdmin,
                command.TransitOfficeId,
                cancellationToken).ConfigureAwait(false)
            : await _repository.RejectAsync(
                command.OtTenantId,
                command.ProcedureInstanceId,
                command.Request.Reason.Trim(),
                command.RejectedBy,
                OtTransitionSource.OtAdmin,
                command.TransitOfficeId,
                cancellationToken).ConfigureAwait(false);

        return updated is null
            ? RejectOtClientProcedureResult.InvalidState()
            : RejectOtClientProcedureResult.Rejected(OtClientProcedureMapper.ToResponse(updated));
    }
}
