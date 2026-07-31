using Flit.Admin.Application.Companies.SignatureVault;
using Flit.Tramites.Application.Storage;

namespace Flit.Infrastructure.Storage;

/// <summary>
/// Implementación del puerto <see cref="ISignatureVaultArtifactStorage"/> (HU #10643, ADR-0025 §3):
/// custodia el artefacto de firma en el storage de la empresa delegando en
/// <see cref="IAttachmentStorage"/> (S3 vía presigned URLs; el SHA-256 lo calcula el backend). El
/// material vive cifrado en reposo en S3; en la fila del baúl solo queda el path + hash.
///
/// TODO composite: v1 persiste el PNG capturado tal cual. El PNG compuesto opcional
/// (firma + nombre + hash + vigencia, análogo a los generadores QuestPDF, ADR-0025 §5) queda para una
/// fase posterior; no bloquea el alta.
/// </summary>
internal sealed class SignatureVaultArtifactStorage : ISignatureVaultArtifactStorage
{
    // El file-manager agrupa los objetos por un id de "instancia" en la metadata; el baúl no tiene
    // procedure_instance, así que se usa el tenant como clave de agrupación del artefacto.
    private const string ArtifactTipo = "signature_vault";
    private const string ArtifactFilename = "signature.png";

    private readonly IAttachmentStorage _storage;

    public SignatureVaultArtifactStorage(IAttachmentStorage storage)
    {
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
    }

    public async Task<StoredSignatureArtifact> SaveAsync(
        Guid tenantId,
        byte[] artifact,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        if (artifact.Length == 0)
        {
            throw new ArgumentException("El artefacto de firma no puede estar vacío.", nameof(artifact));
        }

        using var content = new MemoryStream(artifact, writable: false);
        var stored = await _storage
            .SaveAsync(tenantId, ArtifactTipo, ArtifactFilename, content, cancellationToken)
            .ConfigureAwait(false);

        return new StoredSignatureArtifact(stored.StoragePath, stored.Sha256);
    }
}
