using Flit.Admin.Domain.Companies.MandateSigners;
using Flit.Admin.Domain.Companies.TransitOffices;

namespace Flit.Admin.Application.Companies.MandateSigners.InactivateMandateSigner;

public enum InactivateMandateSignerOutcome
{
    Inactivated,
    NotFound,
}

/// <summary>Desenlace detallado de la inactivación: incluye los conteos de la reasignación de trámites (HU #13137).</summary>
public sealed record InactivateMandateSignerResult(
    InactivateMandateSignerOutcome Outcome,
    MandateSignerLifecycleResult? Lifecycle);

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
        CancellationToken cancellationToken = default) =>
        (await HandleDetailedAsync(command, cancellationToken).ConfigureAwait(false)).Outcome;

    /// <summary>
    /// Igual que <see cref="HandleAsync"/> pero devuelve el detalle: defaults retirados y trámites reasignados o
    /// pendientes de decisión del OT (HU #13135, #13137).
    /// </summary>
    public async Task<InactivateMandateSignerResult> HandleDetailedAsync(
        InactivateMandateSignerCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var signer = await _reader
            .GetByIdAsync(command.MandateSignerId, cancellationToken).ConfigureAwait(false);

        if (signer is null || signer.TransitOfficeId != command.TransitOfficeId || !signer.IsActive)
        {
            return new InactivateMandateSignerResult(InactivateMandateSignerOutcome.NotFound, null);
        }

        // El tenant del OT es necesario para la auditoría; se resuelve aun si el OT está
        // inactivo (la inactivación del mandatario no exige OT operativo).
        var otStatus = await _otStatus
            .GetByIdAsync(command.TransitOfficeId, cancellationToken).ConfigureAwait(false);

        if (otStatus?.TenantId is null)
        {
            return new InactivateMandateSignerResult(InactivateMandateSignerOutcome.NotFound, null);
        }

        var result = await _repository.InactivateAsync(
            new InactivateMandateSignerData(
                command.MandateSignerId,
                otStatus.TenantId.Value,
                command.ChangedBy,
                command.CorrelationId,
                command.ActorKind),
            cancellationToken).ConfigureAwait(false);

        return result.Applied
            ? new InactivateMandateSignerResult(InactivateMandateSignerOutcome.Inactivated, result)
            : new InactivateMandateSignerResult(InactivateMandateSignerOutcome.NotFound, null);
    }
}
