namespace Flit.Tramites.Domain.ValueObjects;

public sealed class ValidationResult
{
    public bool IsValid => Errors.Count == 0;
    public List<ValidationError> Errors { get; } = [];

    public void AddError(string code, string message, string path) =>
        Errors.Add(new ValidationError(code, message, path));
}
