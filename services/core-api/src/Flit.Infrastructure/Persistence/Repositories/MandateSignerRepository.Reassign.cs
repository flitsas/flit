using Flit.Admin.Domain.Companies.MandateSigners;

namespace Flit.Infrastructure.Persistence.Repositories;

/// <summary>
/// HU #13137 (Feature #13115, ADR-0066 P8) — al dar de baja a un mandatario, los trámites radicados sin aprobar que
/// apuntaban a él se reasignan con la prelación del OT (<see cref="IMandateSignerProcedureReassigner"/>) DENTRO de la
/// misma transacción de la baja: si la reasignación falla, se revierte todo y el mandatario sigue activo.
/// </summary>
internal sealed partial class MandateSignerRepository
{
    private async Task<MandateSignerReassignmentResult> ReassignProceduresAsync(
        Guid mandateSignerId,
        CancellationToken cancellationToken) =>
        _reassigner is null
            ? MandateSignerReassignmentResult.None
            : await _reassigner.ReassignAsync(mandateSignerId, cancellationToken).ConfigureAwait(false);
}
