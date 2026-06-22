using Flit.Admin.Domain.OtProfile;

namespace Flit.Admin.Application.OtProfile.UpdateOtFeatureFlag;

/// <summary>
/// Activa/desactiva un feature flag OT (HU #10215 AC3).
/// </summary>
public sealed class UpdateOtFeatureFlagHandler
{
    private readonly IOtFeatureFlagRepository _flagRepository;

    public UpdateOtFeatureFlagHandler(IOtFeatureFlagRepository flagRepository)
    {
        _flagRepository = flagRepository ?? throw new ArgumentNullException(nameof(flagRepository));
    }

    public async Task<UpdateOtFeatureFlagResult> HandleAsync(
        UpdateOtFeatureFlagCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(command.Request);

        var updated = await _flagRepository.UpdateEnabledAsync(
            command.TenantId,
            command.FlagId,
            command.Request.IsEnabled,
            command.ChangedBy,
            cancellationToken).ConfigureAwait(false);

        return updated is null
            ? UpdateOtFeatureFlagResult.NotFound()
            : UpdateOtFeatureFlagResult.Updated(OtProfileMapper.ToFlagResponse(updated));
    }
}
