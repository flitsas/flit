using Flit.Admin.Domain.Companies.MandateSigners;
using Flit.Admin.Domain.Companies.TransitOffices;

namespace Flit.Admin.Application.Companies.MandateSigners.DeleteMandateSigner;

/// <summary>
/// HU #13135 — eliminación (baja lógica) de un mandatario. <see cref="ConfirmImpact"/> es la confirmación
/// explícita del usuario cuando la baja tiene impacto (único activo de una compañía, defaults o trámites).
/// </summary>
public sealed class DeleteMandateSignerCommand
{
    public required Guid TransitOfficeId { get; init; }
    public required Guid MandateSignerId { get; init; }
    public bool ConfirmImpact { get; init; }
    public Guid? ChangedBy { get; init; }
    public Guid? CorrelationId { get; init; }

    /// <summary>HU #13138 — rol de quien elimina, para la bitácora.</summary>
    public MandateSignerActorKind ActorKind { get; init; } = MandateSignerActorKind.None;
}

public enum DeleteMandateSignerOutcome
{
    Deleted,

    /// <summary>No existe, ya estaba eliminado (doble eliminación) o pertenece a otro organismo: 404.</summary>
    NotFound,

    /// <summary>Hay impacto y no se envió <c>confirmarImpacto</c>: 409, no cambia ningún dato.</summary>
    ConfirmationRequired,
}

/// <summary>Resultado de la eliminación: impacto (con <c>ConfirmationRequired</c>) o detalle de lo aplicado.</summary>
public sealed record DeleteMandateSignerResult(
    DeleteMandateSignerOutcome Outcome,
    MandateSignerImpact? Impact,
    MandateSignerLifecycleResult? Lifecycle);

/// <summary>
/// Elimina un mandatario con baja lógica (<c>deleted_at</c>): desaparece de listas y candidatos, retira los defaults
/// que apuntaban a él y reasigna los trámites radicados sin aprobar, conservando el historial de los ya firmados
/// (HU #13135). Un impacto no vacío exige <see cref="DeleteMandateSignerCommand.ConfirmImpact"/>: la advertencia no
/// es solo visual.
/// </summary>
public sealed class DeleteMandateSignerHandler
{
    private readonly ITransitOfficeOperationalStatusReader _otStatus;
    private readonly IMandateSignerReader _reader;
    private readonly IMandateSignerImpactReader _impactReader;
    private readonly IMandateSignerRepository _repository;

    public DeleteMandateSignerHandler(
        ITransitOfficeOperationalStatusReader otStatus,
        IMandateSignerReader reader,
        IMandateSignerImpactReader impactReader,
        IMandateSignerRepository repository)
    {
        _otStatus = otStatus ?? throw new ArgumentNullException(nameof(otStatus));
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
        _impactReader = impactReader ?? throw new ArgumentNullException(nameof(impactReader));
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    public async Task<DeleteMandateSignerResult> HandleAsync(
        DeleteMandateSignerCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        // GetByIdAsync ya excluye a los eliminados: la doble eliminación cae aquí como 404.
        var signer = await _reader
            .GetByIdAsync(command.MandateSignerId, cancellationToken).ConfigureAwait(false);

        if (signer is null || !AppliesToOffice(signer, command.TransitOfficeId))
        {
            return new DeleteMandateSignerResult(DeleteMandateSignerOutcome.NotFound, null, null);
        }

        var impact = await _impactReader
            .GetImpactAsync(command.MandateSignerId, cancellationToken).ConfigureAwait(false);

        if (!impact.IsEmpty && !command.ConfirmImpact)
        {
            return new DeleteMandateSignerResult(DeleteMandateSignerOutcome.ConfirmationRequired, impact, null);
        }

        var otStatus = await _otStatus
            .GetByIdAsync(command.TransitOfficeId, cancellationToken).ConfigureAwait(false);

        if (otStatus?.TenantId is null)
        {
            return new DeleteMandateSignerResult(DeleteMandateSignerOutcome.NotFound, null, null);
        }

        var result = await _repository.DeleteAsync(
            new DeleteMandateSignerData(
                command.MandateSignerId,
                otStatus.TenantId.Value,
                command.ChangedBy,
                command.CorrelationId,
                command.ActorKind),
            cancellationToken).ConfigureAwait(false);

        return result.Applied
            ? new DeleteMandateSignerResult(DeleteMandateSignerOutcome.Deleted, impact, result)
            : new DeleteMandateSignerResult(DeleteMandateSignerOutcome.NotFound, null, null);
    }

    /// <summary>El mandatario aplica al organismo si es su primario o uno de sus organismos vigentes.</summary>
    private static bool AppliesToOffice(MandateSignerItem signer, Guid transitOfficeId) =>
        signer.TransitOfficeId == transitOfficeId || signer.TransitOfficeIds.Contains(transitOfficeId);
}
