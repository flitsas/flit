using Flit.Admin.Domain.OtRequirements;
// El namespace de este archivo tambien se llama OtRequirements: alias para nombrar el tipo de
// dominio sin ambiguedad (mismo patron que DomainOtProfile en GetOtProfileHandler).
using DomainOtRequirements = Flit.Admin.Domain.OtRequirements.OtRequirements;

namespace Flit.Admin.Application.OtRequirements.GetOtRequirements;

/// <summary>
/// Obtiene los requisitos configurables del OT del tenant (HU #10546 / AC lectura). Si el OT no
/// tiene fila, el provider devuelve <see cref="OtRequirements.SafeDefaults"/> — no auto-persiste.
/// </summary>
public sealed class GetOtRequirementsHandler
{
    private readonly IOtRequirementsProvider _provider;

    public GetOtRequirementsHandler(IOtRequirementsProvider provider)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
    }

    public async Task<OtRequirementsResponse> HandleAsync(
        GetOtRequirementsQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var requirements = await _provider
            .ResolveByTenantAsync(query.TenantId, cancellationToken)
            .ConfigureAwait(false);

        // El OT ya viene resuelto por el endpoint (ResolveOtUserScopeAsync), asi que la fila leida
        // es la suya. Solo queda el caso "OT aprovisionado que aun no tiene fila": el provider
        // devuelve SafeDefaults con TransitOfficeId vacio y la UI no sabria de que organismo habla.
        // Se rellena con el organismo consultado; no se persiste nada (un GET no debe escribir).
        if (requirements.TransitOfficeId == Guid.Empty
            && query.TransitOfficeId is Guid officeId
            && officeId != Guid.Empty)
        {
            requirements = new DomainOtRequirements
            {
                TransitOfficeId = officeId,
                RequiresRnmc = requirements.RequiresRnmc,
                AllowPlatePreassign = requirements.AllowPlatePreassign,
                IdentityValidationEnabled = requirements.IdentityValidationEnabled,
            };
        }

        return OtRequirementsMapper.ToResponse(requirements);
    }
}
