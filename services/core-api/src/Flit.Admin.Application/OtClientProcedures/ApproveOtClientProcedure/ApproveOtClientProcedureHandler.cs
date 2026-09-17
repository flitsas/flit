using Flit.Admin.Domain.OtClientProcedures;
using Flit.Admin.Domain.OtProfile;

namespace Flit.Admin.Application.OtClientProcedures.ApproveOtClientProcedure;

/// <summary>Aprueba un trámite entregado de un cliente OT (HU #10217 AC2 · N 03: entregado→aprobado).</summary>
public sealed class ApproveOtClientProcedureHandler
{
    // N 03 (ADR-0022) + ADR-0059: aprobar solo sale de 'entregado' (Preasignación y Asignado son
    // etapas previas de la Ruta Larga; la política única del ciclo de vida lo re-valida en el repo).
    // El literal se conserva porque este proyecto no referencia Flit.Tramites.Domain.
    private const string EstadoEntregado = "entregado";

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
            .GetByIdAsync(
                command.OtTenantId,
                command.ProcedureInstanceId,
                command.TransitOfficeId,
                cancellationToken)
            .ConfigureAwait(false);

        if (existing is null)
        {
            return ApproveOtClientProcedureResult.NotFound();
        }

        if (!string.Equals(existing.Status, EstadoEntregado, StringComparison.Ordinal))
        {
            return ApproveOtClientProcedureResult.InvalidState();
        }

        var updated = await _repository.ApproveAsync(
            command.OtTenantId,
            command.ProcedureInstanceId,
            command.ApprovedBy,
            OtTransitionSource.OtAdmin,
            command.MandateSignerId,
            command.TransitOfficeId,
            cancellationToken).ConfigureAwait(false);

        return updated is null
            ? ApproveOtClientProcedureResult.InvalidState()
            : ApproveOtClientProcedureResult.Approved(OtClientProcedureMapper.ToResponse(updated));
    }
}
