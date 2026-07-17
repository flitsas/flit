using Flit.Admin.Domain.Companies.TransitOffices;

namespace Flit.Infrastructure.OtRules;

/// <summary>
/// Resuelve el OT DESTINO de un trámite: el elegido en el FUR o, si aún no hay, el único grant
/// vigente de la empresa. Lógica compartida por las políticas por-OT
/// (<see cref="IdentityValidationPolicy"/>, <see cref="RnmcRequirementPolicy"/>,
/// <see cref="ConsultationRestrictionPolicy"/>), donde estaba triplicada.
///
/// NO confundir con <see cref="TransitOfficeResolver"/> (puerto <c>ITransitOfficeResolver</c>), que
/// resuelve un OT habilitado POR NOMBRE RUNT contra el catálogo. Este solo elige el OT destino a
/// partir de los grants; no toca catálogo.
///
/// Devolver <c>null</c> (0 o ≥2 grants y sin OT explícito) significa "OT no resoluble": cada política
/// decide su propio default, que NO es uniforme (identidad se exige por seguridad; RNMC y las
/// restricciones de consulta no).
/// </summary>
internal static class TransitOfficeDestinationResolver
{
    public static async Task<Guid?> ResolveAsync(
        ITransitGrantRepository grants,
        Guid tenantId,
        Guid? transitOfficeId,
        CancellationToken cancellationToken)
    {
        if (transitOfficeId is { } explicitOffice && explicitOffice != Guid.Empty)
        {
            return explicitOffice;
        }

        // Aún sin OT en el FUR: si la empresa tiene exactamente un grant vigente, ese es el destino.
        var enabledOffices = await grants
            .ListEnabledOfficeIdsAsync(tenantId, cancellationToken)
            .ConfigureAwait(false);

        return enabledOffices.Count == 1 ? enabledOffices[0] : null;
    }
}
