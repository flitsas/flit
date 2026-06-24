namespace Flit.Admin.Application.OtProfile.UpdateOtFeatureFlag;

public sealed class UpdateOtFeatureFlagCommand
{
    public required Guid TenantId { get; init; }

    public required Guid FlagId { get; init; }

    public Guid? ChangedBy { get; init; }

    public required UpdateOtFeatureFlagRequest Request { get; init; }
}

public sealed class UpdateOtFeatureFlagRequest
{
    public bool IsEnabled { get; init; }
}

public enum UpdateOtFeatureFlagStatus
{
    Updated,
    NotFound,
}

public sealed class UpdateOtFeatureFlagResult
{
    public UpdateOtFeatureFlagStatus Status { get; init; }

    public OtFeatureFlagResponse? Flag { get; init; }

    public static UpdateOtFeatureFlagResult NotFound() =>
        new() { Status = UpdateOtFeatureFlagStatus.NotFound };

    public static UpdateOtFeatureFlagResult Updated(OtFeatureFlagResponse flag) =>
        new() { Status = UpdateOtFeatureFlagStatus.Updated, Flag = flag };
}
