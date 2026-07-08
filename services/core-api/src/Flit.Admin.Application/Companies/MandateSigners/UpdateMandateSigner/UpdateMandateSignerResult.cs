namespace Flit.Admin.Application.Companies.MandateSigners.UpdateMandateSigner;

public enum UpdateMandateSignerOutcome
{
    Updated,
    NotFound,
    ValidationFailed,
}

/// <summary>Resultado de la edición: actualizado, no encontrado (404) o inválido (422).</summary>
public sealed class UpdateMandateSignerResult
{
    private UpdateMandateSignerResult(
        UpdateMandateSignerOutcome outcome,
        string? integrityHash,
        IReadOnlyList<MandateSignerValidationError> errors)
    {
        Outcome = outcome;
        IntegrityHash = integrityHash;
        Errors = errors;
    }

    public UpdateMandateSignerOutcome Outcome { get; }
    public string? IntegrityHash { get; }
    public IReadOnlyList<MandateSignerValidationError> Errors { get; }

    public static UpdateMandateSignerResult Updated(string integrityHash) =>
        new(UpdateMandateSignerOutcome.Updated, integrityHash, []);

    public static UpdateMandateSignerResult NotFound() =>
        new(UpdateMandateSignerOutcome.NotFound, null, []);

    public static UpdateMandateSignerResult Invalid(IReadOnlyList<MandateSignerValidationError> errors) =>
        new(UpdateMandateSignerOutcome.ValidationFailed, null, errors);
}
