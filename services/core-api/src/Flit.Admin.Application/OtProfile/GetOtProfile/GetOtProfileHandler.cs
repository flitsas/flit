using Flit.Admin.Domain.OtProfile;
using DomainOtProfile = Flit.Admin.Domain.OtProfile.OtProfile;

namespace Flit.Admin.Application.OtProfile.GetOtProfile;

/// <summary>
/// Obtiene el perfil OT del tenant autenticado (HU #10215 AC1).
/// Si no existe fila, crea el perfil por defecto (dashboard, read-only false).
/// </summary>
public sealed class GetOtProfileHandler
{
    private readonly IOtProfileRepository _profileRepository;

    public GetOtProfileHandler(IOtProfileRepository profileRepository)
    {
        _profileRepository = profileRepository ?? throw new ArgumentNullException(nameof(profileRepository));
    }

    public async Task<OtProfileResponse> HandleAsync(
        GetOtProfileQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        // SuperAdmin navegando el hub de una OT concreta: se LEE el perfil de esa oficina por
        // transit_office_id, sin crear ni reasignar nada. Un GET no debe mutar; además, crear un
        // perfil para el tenant del SuperAdmin apuntando a una oficina ajena violaría la unicidad
        // uq_transit_office_profiles_transit_office_id (una oficina = un perfil). Si la oficina aún
        // no tiene perfil, se devuelve uno por defecto (dashboard) SIN persistir.
        if (query.TransitOfficeId is Guid officeId && officeId != Guid.Empty)
        {
            var byOffice = await _profileRepository
                .GetByTransitOfficeAsync(officeId, cancellationToken)
                .ConfigureAwait(false);

            return OtProfileMapper.ToResponse(byOffice ?? DefaultProfileFor(officeId));
        }

        // ot_admin (o bootstrap): perfil del tenant autenticado; se crea por defecto si no existe.
        var profile = await _profileRepository
            .GetByTenantAsync(query.TenantId, cancellationToken)
            .ConfigureAwait(false);

        profile ??= await _profileRepository.SaveAsync(
            query.TenantId,
            OtOperationModes.Dashboard,
            quipuxReadOnly: false,
            changedBy: null,
            transitOfficeId: null,
            cancellationToken).ConfigureAwait(false);

        return OtProfileMapper.ToResponse(profile);
    }

    /// <summary>Perfil por defecto (no persistido) para una oficina que aún no tiene fila.</summary>
    private static DomainOtProfile DefaultProfileFor(Guid transitOfficeId) => new()
    {
        Id = Guid.Empty,
        TenantId = Guid.Empty,
        TransitOfficeId = transitOfficeId,
        OperationMode = OtOperationModes.Dashboard,
        QuipuxReadOnly = false,
        FeatureFlags = [],
    };
}
