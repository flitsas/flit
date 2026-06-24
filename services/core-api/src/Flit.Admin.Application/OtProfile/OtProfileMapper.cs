using DomainOtProfile = Flit.Admin.Domain.OtProfile.OtProfile;
using Flit.Admin.Domain.OtProfile;

namespace Flit.Admin.Application.OtProfile;

internal static class OtProfileMapper
{
    public static OtProfileResponse ToResponse(DomainOtProfile profile) => new(
        profile.OperationMode,
        profile.QuipuxReadOnly,
        profile.TransitOfficeId,
        profile.FeatureFlags.Select(ToFlagResponse).ToList());

    public static OtFeatureFlagResponse ToFlagResponse(OtFeatureFlag flag) => new(
        flag.Id,
        flag.FlagKey,
        flag.IsEnabled,
        flag.ConfigJson);
}
