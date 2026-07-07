using Flit.Admin.Domain.OtRequirements;

namespace Flit.Infrastructure.Persistence.Repositories;

/// <summary>
/// Provider de requisitos por OT (HU #10545). Envuelve el repositorio para garantizar el contrato
/// del <see cref="IOtRequirementsProvider"/>: nunca devuelve nulo — si el OT no tiene fila,
/// entrega <see cref="OtRequirements.SafeDefaults"/> (AC1).
/// </summary>
internal sealed class OtRequirementsProvider : IOtRequirementsProvider
{
    private readonly IOtRequirementsRepository _repository;

    public OtRequirementsProvider(IOtRequirementsRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    public async Task<OtRequirements> ResolveByTenantAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        var persisted = await _repository
            .GetByTenantAsync(tenantId, cancellationToken)
            .ConfigureAwait(false);

        return persisted ?? OtRequirements.SafeDefaults(Guid.Empty);
    }
}
