namespace Flit.Admin.Application.Companies.MandateSigners.CreateMandateSigner;

/// <summary>Resultado del alta: válido (con id + huella generada) o inválido (422 + errores).</summary>
public sealed class CreateMandateSignerResult
{
    private CreateMandateSignerResult(
        bool isValid,
        Guid? mandateSignerId,
        string? integrityHash,
        IReadOnlyList<MandateSignerValidationError> errors)
    {
        IsValid = isValid;
        MandateSignerId = mandateSignerId;
        IntegrityHash = integrityHash;
        Errors = errors;
    }

    public bool IsValid { get; }
    public Guid? MandateSignerId { get; }
    public string? IntegrityHash { get; }
    public IReadOnlyList<MandateSignerValidationError> Errors { get; }

    public static CreateMandateSignerResult Success(Guid mandateSignerId, string integrityHash) =>
        new(true, mandateSignerId, integrityHash, []);

    public static CreateMandateSignerResult Invalid(IReadOnlyList<MandateSignerValidationError> errors) =>
        new(false, null, null, errors);
}
