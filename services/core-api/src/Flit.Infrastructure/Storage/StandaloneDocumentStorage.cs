using Flit.Admin.Application.GeneracionDocumental.Ports;
using Flit.Tramites.Application.Storage;

namespace Flit.Infrastructure.Storage;

/// <summary>
/// Adaptador del puerto <see cref="IStandaloneDocumentStorage"/> (Feature #12201,
/// ADR-0056-generacion-documental-standalone): custodia el PDF del documento standalone delegando
/// en <see cref="IAttachmentStorage"/> (file-manager / S3).
///
/// <para><b>El primer parámetro de <c>SaveAsync</c> es una clave de agrupación opaca, no una FK.</b>
/// Se le pasa el <c>tenantId</c>, igual que hacen <see cref="SignatureVaultArtifactStorage"/>,
/// <see cref="IdentitySignatureArtifactStorage"/> y <see cref="DeedDocumentStorage"/> (y
/// <see cref="MandateTemplateStorage"/> con el <c>transitOfficeId</c>). Por eso NO hay ningún acople
/// con trámites que romper aquí: <c>IAttachmentStorage</c> no se modifica.</para>
///
/// <para>El puerto existe además por la restricción de compilación C6: <c>Flit.Admin.Application</c>
/// no referencia <c>Flit.Tramites.Application</c> y no puede nombrar <see cref="IAttachmentStorage"/>.</para>
/// </summary>
internal sealed class StandaloneDocumentStorage : IStandaloneDocumentStorage
{
    private readonly IAttachmentStorage _storage;

    public StandaloneDocumentStorage(IAttachmentStorage storage)
    {
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
    }

    public async Task<StoredStandaloneDocument> SaveAsync(
        Guid tenantId,
        string tipo,
        string filename,
        Stream content,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);

        var stored = await _storage
            .SaveAsync(tenantId, tipo, filename, content, cancellationToken)
            .ConfigureAwait(false);

        return new StoredStandaloneDocument(stored.StoragePath, stored.Sha256, stored.SizeBytes);
    }
}
