using Flit.Admin.Domain.Companies.SignatureVault;

namespace Flit.Admin.Application.Companies.SignatureVault.ListSignatureVault;

/// <summary>
/// Vista de una firma del baúl para la gestión admin. NO expone material de firma (no existe en BD):
/// solo metadatos + la referencia al artefacto (<c>StoragePath</c>/<c>StorageSha256</c>), nunca el
/// binario ni una URL pública (ADR-0025 §3). <c>DocumentNumber</c> es PII (Ley 1581): se entrega solo
/// en respuestas autenticadas de gestión (SuperAdmin) y no debe loguearse. <c>Estado</c> es
/// <c>activa</c>|<c>revocada</c>.
/// </summary>
public sealed record SignatureVaultResponse(
    Guid Id,
    string DocumentType,
    string DocumentNumber,
    string? NitEmpresa,
    string FullName,
    string SignatureHash,
    string StoragePath,
    string StorageSha256,
    string Estado,
    DateOnly VigenciaDesde,
    DateOnly VigenciaHasta,
    Guid? MandateSignerId,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt,
    string? CodigoHash)
{
    public static SignatureVaultResponse From(SignatureVaultItem item) =>
        new(
            item.Id,
            item.DocumentType,
            item.DocumentNumber,
            item.NitEmpresa,
            item.FullName,
            item.SignatureHash,
            item.StoragePath,
            item.StorageSha256,
            item.Estado == SignatureVaultEstado.Activa ? "activa" : "revocada",
            item.VigenciaDesde,
            item.VigenciaHasta,
            item.MandateSignerId,
            item.CreatedAt,
            item.UpdatedAt,
            item.CodigoHash);
}
