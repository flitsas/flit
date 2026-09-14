using Flit.Admin.Domain.Companies.TransitOffices;
using Flit.Tramites.Domain.Integration;

namespace Flit.Infrastructure.OtRules;

/// <summary>
/// Implementación del puerto <see cref="ITransitOfficeGrantGate"/>: resuelve los grants
/// de OT de la empresa (admin.tenant_transit_office_grants) vía <see cref="ITransitGrantRepository"/>.
/// </summary>
internal sealed class TransitOfficeGrantGate : ITransitOfficeGrantGate
{
    private readonly IEffectiveTransitOfficeListResolver _effectiveList;

    public TransitOfficeGrantGate(IEffectiveTransitOfficeListResolver effectiveList)
    {
        _effectiveList = effectiveList ?? throw new ArgumentNullException(nameof(effectiveList));
    }

    public async Task<bool> IsEnabledForTenantAsync(
        Guid tenantId,
        Guid transitOfficeId,
        CancellationToken cancellationToken = default)
    {
        var enabled = await _effectiveList
            .ListEffectiveOfficeIdsAsync(tenantId, cancellationToken)
            .ConfigureAwait(false);

        return enabled.Contains(transitOfficeId);
    }
}
