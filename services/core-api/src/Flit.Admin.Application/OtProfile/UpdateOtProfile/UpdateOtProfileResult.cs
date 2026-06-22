namespace Flit.Admin.Application.OtProfile.UpdateOtProfile;

public sealed record OtProfileValidationError(string Field, string Message);

public sealed class UpdateOtProfileResult
{
    public bool IsValid { get; init; }

    public OtProfileResponse? Profile { get; init; }

    public IReadOnlyList<OtProfileValidationError> Errors { get; init; } = [];

    public static UpdateOtProfileResult Invalid(IReadOnlyList<OtProfileValidationError> errors) =>
        new() { IsValid = false, Errors = errors };

    public static UpdateOtProfileResult Success(OtProfileResponse profile) =>
        new() { IsValid = true, Profile = profile };
}
