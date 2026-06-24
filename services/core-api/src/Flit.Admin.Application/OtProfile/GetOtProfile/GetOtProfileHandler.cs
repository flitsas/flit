using Flit.Admin.Domain.OtProfile;

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

        var profile = await _profileRepository
            .GetByTenantAsync(query.TenantId, cancellationToken)
            .ConfigureAwait(false);

        if (profile is null)
        {
            profile = await _profileRepository.SaveAsync(
                query.TenantId,
                OtOperationModes.Dashboard,
                quipuxReadOnly: false,
                changedBy: null,
                cancellationToken).ConfigureAwait(false);
        }

        return OtProfileMapper.ToResponse(profile);
    }
}
