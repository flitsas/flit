using Flit.Admin.Domain.OtClientProcedures;
using Flit.Admin.Domain.OtProfile;

namespace Flit.Admin.Application.OtClientProcedures.RevokeOtClientProcedure;

/// <summary>
/// Revoca un trámite Aprobado de un cliente OT (HU #12166, Feature #12156 · N 03: aprobado→revocado).
/// Libera la placa y marca el FUR/certificados vigentes como históricos en la misma transacción (ver
/// <see cref="IOtClientProcedureRepository.RevokeAsync"/>). El admin de FLIT NO tiene esta acción: solo
/// el OT puede deshacer su propia aprobación.
/// </summary>
public sealed class RevokeOtClientProcedureHandler
{
    private const string EstadoAprobado = "aprobado";

    private readonly IOtClientProcedureRepository _repository;
    private readonly IQuipuxReadOnlyGuard _quipuxReadOnlyGuard;

    public RevokeOtClientProcedureHandler(
        IOtClientProcedureRepository repository,
        IQuipuxReadOnlyGuard quipuxReadOnlyGuard)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _quipuxReadOnlyGuard = quipuxReadOnlyGuard
            ?? throw new ArgumentNullException(nameof(quipuxReadOnlyGuard));
    }

    public async Task<RevokeOtClientProcedureResult> HandleAsync(
        RevokeOtClientProcedureCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var guardResult = await _quipuxReadOnlyGuard
            .ValidateActionAsync(command.OtTenantId, "revocar", cancellationToken)
            .ConfigureAwait(false);
        if (!guardResult.IsAllowed)
        {
            return RevokeOtClientProcedureResult.QuipuxReadOnly();
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
            return RevokeOtClientProcedureResult.NotFound();
        }

        // AC2 — fuera de 'aprobado' se rechaza. La comprobación real (autoridad) vive en
        // TramiteStateMachine dentro del repositorio; este chequeo evita el roundtrip cuando ya se
        // sabe que no aplica.
        if (!string.Equals(existing.Status, EstadoAprobado, StringComparison.Ordinal))
        {
            return RevokeOtClientProcedureResult.InvalidState();
        }

        var updated = await _repository.RevokeAsync(
            command.OtTenantId,
            command.ProcedureInstanceId,
            command.Reason?.Trim(),
            command.RevokedBy,
            OtTransitionSource.OtAdmin,
            command.TransitOfficeId,
            cancellationToken).ConfigureAwait(false);

        return updated is null
            ? RevokeOtClientProcedureResult.InvalidState()
            : RevokeOtClientProcedureResult.Revoked(OtClientProcedureMapper.ToResponse(updated));
    }
}
