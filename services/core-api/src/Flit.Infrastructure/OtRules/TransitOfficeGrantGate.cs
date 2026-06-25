using Flit.Admin.Domain.Companies.TransitOffices;
using Flit.Tramites.Domain.Integration;

namespace Flit.Infrastructure.OtRules;

/// <summary>
/// Implementación del puerto <see cref="ITransitOfficeGrantGate"/>: resuelve los grants
/// de OT de la empresa (admin.tenant_transit_office_grants) vía <see cref="ITransitGrantRepository"/>.
/// </summary>
internal sealed class TransitOfficeGrantGate : ITransitOfficeGrantGate
{
    private readonly ITransitGrantRepository _grants;

    public TransitOfficeGrantGate(ITransitGrantRepository grants)
    {
        _grants = grants ?? throw new ArgumentNullException(nameof(grants));
    }

    public async Task<bool> IsEnabledForTenantAsync(
        Guid tenantId,
        Guid transitOfficeId,
        CancellationToken cancellationToken = default)
    {
        var enabled = await _grants
            .ListEnabledOfficeIdsAsync(tenantId, cancellationToken)
            .ConfigureAwait(false);

        return enabled.Contains(transitOfficeId);
    }
}
