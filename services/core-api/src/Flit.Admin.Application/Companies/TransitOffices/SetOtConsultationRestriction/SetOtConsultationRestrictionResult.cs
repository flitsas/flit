namespace Flit.Admin.Application.Companies.TransitOffices.SetOtConsultationRestriction;

/// <summary>
/// Resultado de fijar una restricción de consulta por OT (AC1–AC4). <see cref="IsValid"/>
/// falso indica que alguna validación previa a tocar BD falló (→ 422).
/// </summary>
public sealed class SetOtConsultationRestrictionResult
{
    private SetOtConsultationRestrictionResult(
        bool isValid,
        IReadOnlyList<OtConsultationRestrictionValidationError> errors)
    {
        IsValid = isValid;
        Errors = errors;
    }

    public bool IsValid { get; }

    public IReadOnlyList<OtConsultationRestrictionValidationError> Errors { get; }

    public static SetOtConsultationRestrictionResult Success() => new(true, []);

    public static SetOtConsultationRestrictionResult Invalid(
        IReadOnlyList<OtConsultationRestrictionValidationError> errors) => new(false, errors);
}
