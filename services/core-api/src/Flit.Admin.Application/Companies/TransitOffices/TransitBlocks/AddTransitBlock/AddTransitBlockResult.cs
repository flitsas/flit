namespace Flit.Admin.Application.Companies.TransitOffices.TransitBlocks.AddTransitBlock;

public sealed class AddTransitBlockResult
{
    private AddTransitBlockResult(bool isValid, bool added, IReadOnlyList<TransitBlockValidationError>? errors)
    {
        IsValid = isValid;
        Added = added;
        Errors = errors ?? [];
    }

    public bool IsValid { get; }

    public bool Added { get; }

    public IReadOnlyList<TransitBlockValidationError> Errors { get; }

    public static AddTransitBlockResult Success(bool added) => new(true, added, null);

    public static AddTransitBlockResult Invalid(IReadOnlyList<TransitBlockValidationError> errors) =>
        new(false, false, errors);
}
