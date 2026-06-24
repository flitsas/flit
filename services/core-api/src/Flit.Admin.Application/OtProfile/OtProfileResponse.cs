namespace Flit.Admin.Application.OtProfile;

public sealed record OtFeatureFlagResponse(
    Guid Id,
    string FlagKey,
    bool IsEnabled,
    string Config);

public sealed record OtProfileResponse(
    string OperationMode,
    bool QuipuxReadOnly,
    Guid TransitOfficeId,
    IReadOnlyList<OtFeatureFlagResponse> FeatureFlags);
