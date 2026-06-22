namespace Flit.Admin.Domain.OtProfile;

public interface IOtFeatureFlagRepository
{
    Task<IReadOnlyList<OtFeatureFlag>> ListByTenantAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default);

    Task<OtFeatureFlag?> GetByIdAsync(
        Guid tenantId,
        Guid flagId,
        CancellationToken cancellationToken = default);

    Task<OtFeatureFlag?> UpdateEnabledAsync(
        Guid tenantId,
        Guid flagId,
        bool isEnabled,
        Guid? changedBy,
        CancellationToken cancellationToken = default);
}
