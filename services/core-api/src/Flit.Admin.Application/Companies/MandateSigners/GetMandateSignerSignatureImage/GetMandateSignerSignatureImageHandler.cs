using Flit.Admin.Application.Companies.SignatureVault;
using Flit.Admin.Domain.Companies.MandateSigners;
using Flit.Admin.Domain.Companies.SignatureVault;

namespace Flit.Admin.Application.Companies.MandateSigners.GetMandateSignerSignatureImage;

public enum GetMandateSignerSignatureImageOutcome
{
    Ok,
    NotFound,
    NoSignature,
}

public sealed record GetMandateSignerSignatureImageResult(
    GetMandateSignerSignatureImageOutcome Outcome,
    byte[]? Content);

/// <summary>
/// PNG del baúl vinculado al mandatario, para el ojo del grid de mandatos del OT.
/// No consulta <c>signature_vault_enabled</c> de ninguna compañía.
/// </summary>
public sealed class GetMandateSignerSignatureImageHandler(
    IMandateSignerReader signers,
    ISignatureVaultReader vault,
    ISignatureVaultArtifactStorage artifacts)
{
    public async Task<GetMandateSignerSignatureImageResult> HandleAsync(
        Guid transitOfficeId,
        Guid mandateSignerId,
        CancellationToken cancellationToken = default)
    {
        var signer = await signers.GetByIdAsync(mandateSignerId, cancellationToken).ConfigureAwait(false);
        if (signer is null || !PerteneceAlOt(signer, transitOfficeId))
            return new(GetMandateSignerSignatureImageOutcome.NotFound, null);

        if (signer.SignatureVaultId is not { } vaultId)
            return new(GetMandateSignerSignatureImageOutcome.NoSignature, null);

        var item = await vault.GetByIdAnyTenantAsync(vaultId, cancellationToken).ConfigureAwait(false);
        if (item is null || string.IsNullOrWhiteSpace(item.StoragePath))
            return new(GetMandateSignerSignatureImageOutcome.NoSignature, null);

        var stream = await artifacts.OpenReadAsync(item.StoragePath, cancellationToken).ConfigureAwait(false);
        if (stream is null)
            return new(GetMandateSignerSignatureImageOutcome.NoSignature, null);

        await using (stream.ConfigureAwait(false))
        {
            using var ms = new MemoryStream();
            await stream.CopyToAsync(ms, cancellationToken).ConfigureAwait(false);
            if (ms.Length == 0)
                return new(GetMandateSignerSignatureImageOutcome.NoSignature, null);

            return new(GetMandateSignerSignatureImageOutcome.Ok, ms.ToArray());
        }
    }

    private static bool PerteneceAlOt(MandateSignerItem signer, Guid transitOfficeId) =>
        signer.TransitOfficeId == transitOfficeId
        || signer.TransitOfficeIds.Contains(transitOfficeId);
}
