namespace Flit.Tramites.Application.Identity;

/// <summary>Referencia al PNG de rúbrica en storage (nunca el binario en BD).</summary>
public sealed record StoredIdentitySignature(string StoragePath, string Sha256);

/// <summary>Custodia del recorte de firma de identidad. Espejo del baúl (ADR-0025 / ADR-0054).</summary>
public interface IIdentitySignatureArtifactStorage
{
    Task<StoredIdentitySignature> SaveAsync(Guid tenantId, byte[] png, CancellationToken cancellationToken = default);

    Task<Stream?> OpenReadAsync(string storagePath, CancellationToken cancellationToken = default);
}
