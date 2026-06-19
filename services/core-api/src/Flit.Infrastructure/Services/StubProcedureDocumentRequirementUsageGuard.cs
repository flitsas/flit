using Flit.Admin.Domain.DocumentRequirements;

namespace Flit.Infrastructure.Services;

/// <summary>
/// Implementación transitoria de <see cref="IProcedureDocumentRequirementUsageGuard"/>
/// (HU #10195, AC4). El módulo de instancias de trámite (snapshots) todavía no existe,
/// por lo que este placeholder reporta que ninguna asociación está en uso y permite el
/// borrado. La comprobación real se sustituye en tests y se reemplazará al integrar la
/// HU #10197.
/// </summary>
internal sealed class StubProcedureDocumentRequirementUsageGuard : IProcedureDocumentRequirementUsageGuard
{
    public Task<bool> IsInUseAsync(
        Guid requirementId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(false);
}
