using Flit.Admin.Domain.Companies.MandateSigners;
using Flit.Admin.Domain.Companies.TransitOffices;

namespace Flit.Admin.Application.Companies.MandateSigners.ReactivateMandateSigner;

public enum ReactivateMandateSignerOutcome
{
    Reactivated,
    NotFound,
}

/// <summary>
/// Reactiva un mandatario inactivado: vuelve activo con auditoría atómica (RF28), sin
/// restaurar compañías (se liberaron al inactivar y se reasignan con "Editar"). Idempotente:
/// 404 si no existe, pertenece a otro OT o ya estaba activo.
/// </summary>
public sealed class ReactivateMandateSignerHandler
{
    private readonly ITransitOfficeOperationalStatusReader _otStatus;
    private readonly IMandateSignerReader _reader;
    private readonly IMandateSignerRepository _repository;

    public ReactivateMandateSignerHandler(
        ITransitOfficeOperationalStatusReader otStatus,
        IMandateSignerReader reader,
        IMandateSignerRepository repository)
    {
        _otStatus = otStatus ?? throw new ArgumentNullException(nameof(otStatus));
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    public async Task<ReactivateMandateSignerOutcome> HandleAsync(
        ReactivateMandateSignerCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var signer = await _reader
            .GetByIdAsync(command.MandateSignerId, cancellationToken).ConfigureAwait(false);

        // 404 si no existe, pertenece a otro OT o ya estaba activo.
        if (signer is null || signer.TransitOfficeId != command.TransitOfficeId || signer.IsActive)
        {
            return ReactivateMandateSignerOutcome.NotFound;
        }

        var otStatus = await _otStatus
            .GetByIdAsync(command.TransitOfficeId, cancellationToken).ConfigureAwait(false);

        if (otStatus?.TenantId is null)
        {
            return ReactivateMandateSignerOutcome.NotFound;
        }

        var reactivated = await _repository.ReactivateAsync(
            new ReactivateMandateSignerData(
                command.MandateSignerId,
                otStatus.TenantId.Value,
                command.ChangedBy,
                command.CorrelationId),
            cancellationToken).ConfigureAwait(false);

        return reactivated
            ? ReactivateMandateSignerOutcome.Reactivated
            : ReactivateMandateSignerOutcome.NotFound;
    }
}
