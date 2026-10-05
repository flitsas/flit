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
        IReadOnlyList<MandateSignerValidationError> errors,
        Flit.Admin.Application.Companies.MandateSigners.CreateMandateSigner.MandateSignerIdentityOutcome identity =
            Flit.Admin.Application.Companies.MandateSigners.CreateMandateSigner.MandateSignerIdentityOutcome.NotAttempted)
    {
        Identity = identity;
        Outcome = outcome;
        IntegrityHash = integrityHash;
        Errors = errors;
    }

    public UpdateMandateSignerOutcome Outcome { get; }
    public string? IntegrityHash { get; }
    public IReadOnlyList<MandateSignerValidationError> Errors { get; }

    /// <summary>
    /// HU #13246 — desenlace de la validación de identidad propia lanzada por la edición (cambio de documento o paso de
    /// baúl a biometría); <c>NotAttempted</c> cuando la edición no la dispara.
    /// </summary>
    public Flit.Admin.Application.Companies.MandateSigners.CreateMandateSigner.MandateSignerIdentityOutcome Identity { get; }

    public static UpdateMandateSignerResult Updated(
        string integrityHash,
        Flit.Admin.Application.Companies.MandateSigners.CreateMandateSigner.MandateSignerIdentityOutcome identity =
            Flit.Admin.Application.Companies.MandateSigners.CreateMandateSigner.MandateSignerIdentityOutcome.NotAttempted) =>
        new(UpdateMandateSignerOutcome.Updated, integrityHash, [], identity);

    public static UpdateMandateSignerResult NotFound() =>
        new(UpdateMandateSignerOutcome.NotFound, null, []);

    public static UpdateMandateSignerResult Invalid(IReadOnlyList<MandateSignerValidationError> errors) =>
        new(UpdateMandateSignerOutcome.ValidationFailed, null, errors);
}
