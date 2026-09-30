using Flit.Admin.Domain.Companies.MandateSigners;

namespace Flit.Infrastructure.Persistence.Repositories;

/// <summary>
/// Punto de extensión de la reasignación de trámites al dar de baja (HU #13137): por ahora no toca trámites.
/// </summary>
internal sealed partial class MandateSignerRepository
{
    private static Task<MandateSignerReassignmentResult> ReassignProceduresAsync(
        Guid mandateSignerId,
        CancellationToken cancellationToken) =>
        Task.FromResult(MandateSignerReassignmentResult.None);
}
