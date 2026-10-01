using Flit.Admin.Domain.Companies.MandateSigners;
using Flit.Admin.Domain.Companies.TransitOffices;

namespace Flit.Admin.Application.Companies.MandateSigners.ReactivateMandateSigner;

public enum ReactivateMandateSignerOutcome
{
    Reactivated,
    NotFound,

    /// <summary>
    /// Todos los vínculos que recuperaría chocan con otro mandatario activo del mismo origen: no se reactiva nada
    /// (409 <c>mandatario_activo_existente</c>, HU #13136).
    /// </summary>
    Conflict,
}

/// <summary>Desenlace detallado de la reactivación: vínculos restaurados y los que quedaron inactivos por conflicto.</summary>
public sealed record ReactivateMandateSignerResult(
    ReactivateMandateSignerOutcome Outcome,
    MandateSignerLifecycleResult? Lifecycle);

/// <summary>
/// Reactiva un mandatario inactivado: vuelve activo con auditoría atómica (RF28) y recupera los vínculos
/// con compañías y organismos que retiró la baja, sin desplazar al default vigente (HU #13136). Idempotente:
/// 404 si no existe, está eliminado, pertenece a otro OT o ya estaba activo.
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
        CancellationToken cancellationToken = default) =>
        (await HandleDetailedAsync(command, cancellationToken).ConfigureAwait(false)).Outcome;

    /// <summary>Igual que <see cref="HandleAsync"/> pero informa los vínculos restaurados y los conflictos.</summary>
    public async Task<ReactivateMandateSignerResult> HandleDetailedAsync(
        ReactivateMandateSignerCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var signer = await _reader
            .GetByIdAsync(command.MandateSignerId, cancellationToken).ConfigureAwait(false);

        // 404 si no existe, pertenece a otro OT o ya estaba activo.
        if (signer is null || signer.TransitOfficeId != command.TransitOfficeId || signer.IsActive)
        {
            return new ReactivateMandateSignerResult(ReactivateMandateSignerOutcome.NotFound, null);
        }

        var otStatus = await _otStatus
            .GetByIdAsync(command.TransitOfficeId, cancellationToken).ConfigureAwait(false);

        if (otStatus?.TenantId is null)
        {
            return new ReactivateMandateSignerResult(ReactivateMandateSignerOutcome.NotFound, null);
        }

        var result = await _repository.ReactivateAsync(
            new ReactivateMandateSignerData(
                command.MandateSignerId,
                otStatus.TenantId.Value,
                command.ChangedBy,
                command.CorrelationId,
                command.ActorKind),
            cancellationToken).ConfigureAwait(false);

        if (result.AllLinksConflict)
        {
            return new ReactivateMandateSignerResult(ReactivateMandateSignerOutcome.Conflict, result);
        }

        return result.Applied
            ? new ReactivateMandateSignerResult(ReactivateMandateSignerOutcome.Reactivated, result)
            : new ReactivateMandateSignerResult(ReactivateMandateSignerOutcome.NotFound, null);
    }
}
