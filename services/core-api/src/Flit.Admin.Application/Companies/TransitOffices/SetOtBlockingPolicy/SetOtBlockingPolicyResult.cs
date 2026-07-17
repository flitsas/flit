namespace Flit.Admin.Application.Companies.TransitOffices.SetOtBlockingPolicy;

/// <summary>
/// Resultado de fijar una política de bloqueo por OT (AC1–AC4). <see cref="IsValid"/> falso indica
/// que alguna validación previa a tocar BD falló (→ 422).
/// </summary>
public sealed class SetOtBlockingPolicyResult
{
    private SetOtBlockingPolicyResult(
        bool isValid,
        IReadOnlyList<OtBlockingPolicyValidationError> errors)
    {
        IsValid = isValid;
        Errors = errors;
    }

    public bool IsValid { get; }

    public IReadOnlyList<OtBlockingPolicyValidationError> Errors { get; }

    public static SetOtBlockingPolicyResult Success() => new(true, []);

    public static SetOtBlockingPolicyResult Invalid(
        IReadOnlyList<OtBlockingPolicyValidationError> errors) => new(false, errors);
}
