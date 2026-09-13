using Flit.Admin.Domain.Companies.TransitOffices;

namespace Flit.Admin.Application.Companies.TransitOffices.GetTransitGrants;

/// <summary>
/// Caso de uso de lectura de los grants habilitados de un tenant (HU #10192, AC5).
/// Devuelve los ids de organismos habilitados; lista vacía si no hay ninguno.
/// </summary>
public sealed class GetTransitGrantsHandler
{
    private readonly IEffectiveTransitOfficeListResolver _effectiveList;

    public GetTransitGrantsHandler(IEffectiveTransitOfficeListResolver effectiveList)
    {
        _effectiveList = effectiveList ?? throw new ArgumentNullException(nameof(effectiveList));
    }

    public async Task<TransitGrantsResponse> HandleAsync(
        GetTransitGrantsQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var ids = await _effectiveList
            .ListEffectiveOfficeIdsAsync(query.TenantId, cancellationToken)
            .ConfigureAwait(false);

        return new TransitGrantsResponse(ids);
    }
}
