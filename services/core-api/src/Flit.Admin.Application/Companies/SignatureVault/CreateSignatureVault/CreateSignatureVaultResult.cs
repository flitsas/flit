namespace Flit.Admin.Application.Companies.SignatureVault.CreateSignatureVault;

/// <summary>Resultado del alta: válido (con id generado) o inválido (422 + errores).</summary>
public sealed class CreateSignatureVaultResult
{
    private CreateSignatureVaultResult(
        bool isValid,
        Guid? signatureVaultId,
        IReadOnlyList<SignatureVaultValidationError> errors)
    {
        IsValid = isValid;
        SignatureVaultId = signatureVaultId;
        Errors = errors;
    }

    public bool IsValid { get; }
    public Guid? SignatureVaultId { get; }
    public IReadOnlyList<SignatureVaultValidationError> Errors { get; }

    public static CreateSignatureVaultResult Success(Guid signatureVaultId) =>
        new(true, signatureVaultId, []);

    public static CreateSignatureVaultResult Invalid(IReadOnlyList<SignatureVaultValidationError> errors) =>
        new(false, null, errors);
}
