using Flit.Admin.Domain.Companies.MandateSigners;
using Flit.Admin.Domain.Companies.TransitOffices;

namespace Flit.Admin.Application.Companies.MandateSigners.InactivateMandateSigner;

public enum InactivateMandateSignerOutcome
{
    Inactivated,
    NotFound,
}

/// <summary>
/// Inactiva un mandatario (RF24, baja lógica): marca inactivo y libera sus compañías para
/// reasignación, con auditoría atómica (RF28). Idempotente: 404 si no existe, pertenece a otro
/// OT o ya estaba inactivo.
/// </summary>
public sealed class InactivateMandateSignerHandler
{
    private readonly ITransitOfficeOperationalStatusReader _otStatus;
    private readonly IMandateSignerReader _reader;
    private readonly IMandateSignerRepository _repository;

    public InactivateMandateSignerHandler(
        ITransitOfficeOperationalStatusReader otStatus,
        IMandateSignerReader reader,
        IMandateSignerRepository repository)
    {
        _otStatus = otStatus ?? throw new ArgumentNullException(nameof(otStatus));
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    public async Task<InactivateMandateSignerOutcome> HandleAsync(
        InactivateMandateSignerCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var signer = await _reader
            .GetByIdAsync(command.MandateSignerId, cancellationToken).ConfigureAwait(false);

        if (signer is null || signer.TransitOfficeId != command.TransitOfficeId || !signer.IsActive)
        {
            return InactivateMandateSignerOutcome.NotFound;
        }

        // El tenant del OT es necesario para la auditoría; se resuelve aun si el OT está
        // inactivo (la inactivación del mandatario no exige OT operativo).
        var otStatus = await _otStatus
            .GetByIdAsync(command.TransitOfficeId, cancellationToken).ConfigureAwait(false);

        if (otStatus?.TenantId is null)
        {
            return InactivateMandateSignerOutcome.NotFound;
        }

        var inactivated = await _repository.InactivateAsync(
            new InactivateMandateSignerData(
                command.MandateSignerId,
                otStatus.TenantId.Value,
                command.ChangedBy,
                command.CorrelationId),
            cancellationToken).ConfigureAwait(false);

        return inactivated
            ? InactivateMandateSignerOutcome.Inactivated
            : InactivateMandateSignerOutcome.NotFound;
    }
}
