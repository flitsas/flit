namespace Flit.Admin.Application.OtRequirements.UpdateOtRequirements;

public sealed class UpdateOtRequirementsResult
{
    public bool IsValid { get; init; }

    public OtRequirementsResponse? Requirements { get; init; }

    public static UpdateOtRequirementsResult Success(OtRequirementsResponse requirements) =>
        new() { IsValid = true, Requirements = requirements };
}
