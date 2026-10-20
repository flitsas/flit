using Flit.Admin.Domain.Companies.TransitOffices;

namespace Flit.Admin.Application.Companies.TransitOffices;

/// <summary>
/// ¿El OT está habilitado para la compañía? Grant propio, o la lista efectiva de su red: una cabeza
/// Marca Blanca no tiene grants (opera en «operables menos bloqueados»), así que sin esta segunda
/// mirada «Configurar» un OT habilitado de la MB respondía 422 «no está habilitado».
/// </summary>
internal static class TransitOfficeEnabledCheck
{
    public static async Task<bool> IsEnabledAsync(
        ITransitGrantRepository grants,
        IEffectiveTransitOfficeListResolver? effective,
        Guid tenantId,
        Guid transitOfficeId,
        CancellationToken cancellationToken)
    {
        var granted = await grants.ListEnabledOfficeIdsAsync(tenantId, cancellationToken).ConfigureAwait(false);
        if (granted.Contains(transitOfficeId))
        {
            return true;
        }

        if (effective is null)
        {
            return false;
        }

        var effectiveIds = await effective
            .ListEffectiveOfficeIdsAsync(tenantId, cancellationToken)
            .ConfigureAwait(false);
        return effectiveIds.Contains(transitOfficeId);
    }
}
