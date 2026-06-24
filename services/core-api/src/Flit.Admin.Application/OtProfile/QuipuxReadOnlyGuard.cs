using Flit.Admin.Domain.OtProfile;

namespace Flit.Admin.Application.OtProfile;

/// <summary>
/// Guard de acciones en modo Quipux read-only (HU #10215 AC4).
/// Bloquea <c>aprobar</c> y <c>rechazar</c> cuando el perfil OT está en QX read-only.
/// </summary>
public sealed class QuipuxReadOnlyGuard : IQuipuxReadOnlyGuard
{
    private static readonly HashSet<string> RestrictedActions =
        new(StringComparer.OrdinalIgnoreCase) { "aprobar", "rechazar" };

    private readonly IOtProfileRepository _profileRepository;

    public QuipuxReadOnlyGuard(IOtProfileRepository profileRepository)
    {
        _profileRepository = profileRepository ?? throw new ArgumentNullException(nameof(profileRepository));
    }

    public async Task<QuipuxReadOnlyResult> ValidateActionAsync(
        Guid tenantId,
        string action,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(action) || !RestrictedActions.Contains(action))
        {
            return QuipuxReadOnlyResult.Allowed();
        }

        var profile = await _profileRepository
            .GetByTenantAsync(tenantId, cancellationToken)
            .ConfigureAwait(false);

        if (profile is null)
        {
            return QuipuxReadOnlyResult.Allowed();
        }

        if (profile.OperationMode == OtOperationModes.Quipux && profile.QuipuxReadOnly)
        {
            return QuipuxReadOnlyResult.Forbidden();
        }

        return QuipuxReadOnlyResult.Allowed();
    }
}
