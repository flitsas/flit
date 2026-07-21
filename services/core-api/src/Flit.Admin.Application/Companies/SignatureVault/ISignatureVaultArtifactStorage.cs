namespace Flit.Admin.Application.Companies.SignatureVault;

/// <summary>
/// Referencia al artefacto de firma persistido en storage (S3): id opaco (<c>StoragePath</c>) +
/// integridad (<c>Sha256</c>, hex minúsculas). El material NUNCA se guarda en BD (ADR-0025 §3).
/// </summary>
public sealed record StoredSignatureArtifact(string StoragePath, string Sha256);

/// <summary>
/// Puerto para custodiar el artefacto de firma (PNG capturado) en el storage de la empresa. La
/// implementación (Infrastructure) delega en <c>IAttachmentStorage</c> (S3 vía presigned URLs,
/// SHA-256 calculado por el backend). Aísla al módulo Admin del módulo de trámites: el handler del
/// baúl no referencia <c>IAttachmentStorage</c> directamente, solo este puerto acotado. El material
/// se persiste en S3; en la fila del baúl solo queda <c>StoragePath</c> + <c>Sha256</c>.
/// </summary>
public interface ISignatureVaultArtifactStorage
{
    /// <summary>
    /// Persiste los bytes del artefacto de firma y devuelve su referencia de storage + hash de
    /// integridad. <paramref name="artifact"/> son los bytes del PNG capturado (no vacío).
    /// </summary>
    Task<StoredSignatureArtifact> SaveAsync(
        Guid tenantId,
        byte[] artifact,
        CancellationToken cancellationToken = default);
}
