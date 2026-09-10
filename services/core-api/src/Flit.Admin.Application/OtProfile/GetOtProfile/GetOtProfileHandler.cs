using Flit.Admin.Domain.OtProfile;
using DomainOtProfile = Flit.Admin.Domain.OtProfile.OtProfile;

namespace Flit.Admin.Application.OtProfile.GetOtProfile;

/// <summary>
/// Obtiene el perfil OT del tenant autenticado (HU #10215 AC1).
/// Solo LEE: si no existe fila devuelve un perfil por defecto sin persistirlo.
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

        // ot_admin: perfil del tenant autenticado.
        var profile = await _profileRepository
            .GetByTenantAsync(query.TenantId, cancellationToken)
            .ConfigureAwait(false);

        // Un GET NO debe mutar. Hasta aquí, cuando el tenant no tenía perfil se le CREABA uno
        // (changedBy: null, y la oficina adivinada por el repositorio: primer grant o centinela).
        // Bastaba con que alguien abriera el hub del OT para que su tenant quedara convertido en
        // organismo de tránsito, sin decisión ni autor. Así fue como el tenant del SuperAdmin
        // («Empresa Demo FLIT») acabó siendo el OT de Barranquilla, lo que además lo excluye de
        // Consultas (SuperAdminTenantScope descarta a los tenants con perfil OT).
        //
        // El alta legítima de un OT ocurre en TransitOfficeTenantWriteRepository (consola de
        // activación, con autor) y en los seeds de dev; este bootstrap implícito era redundante.
        // Sin fila se devuelve un perfil por defecto SIN persistir, igual que la rama de SuperAdmin.
        if (profile is null)
        {
            return OtProfileMapper.ToResponse(DefaultProfileFor(Guid.Empty));
        }

        return OtProfileMapper.ToResponse(profile);
    }

    /// <summary>Perfil por defecto (no persistido) para una oficina que aún no tiene fila.</summary>
    internal static DomainOtProfile DefaultProfileFor(Guid transitOfficeId) => new()
    {
        Id = Guid.Empty,
        TenantId = Guid.Empty,
        TransitOfficeId = transitOfficeId,
        OperationMode = OtOperationModes.Dashboard,
        QuipuxReadOnly = false,
        FeatureFlags = [],
    };
}

