using Flit.Tramites.Application.Storage;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Repositories;

namespace Flit.Tramites.Application.UseCases.ImprintSignatures;

/// <summary>
/// Emite una URL presignada inline del PDF firmado de una impronta (HU #12173).
/// Prefiere <c>signed_storage_path</c> (snapshot); si falta, resuelve el adjunto vigente.
/// </summary>
public sealed class GetImprintSignaturePreviewUrlHandler
{
    private readonly IVehicleSignatureImprintRepository _imprintRepository;
    private readonly IProcedureInstanceRepository _instanceRepository;
    private readonly IAttachmentStorage _storage;

    public GetImprintSignaturePreviewUrlHandler(
        IVehicleSignatureImprintRepository imprintRepository,
        IProcedureInstanceRepository instanceRepository,
        IAttachmentStorage storage)
    {
        _imprintRepository = imprintRepository ?? throw new ArgumentNullException(nameof(imprintRepository));
        _instanceRepository = instanceRepository ?? throw new ArgumentNullException(nameof(instanceRepository));
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
    }

    public async Task<(AttachmentPreviewUrlResult? Result, string? Error)> HandleAsync(
        Guid vehicleSignatureImprintId,
        CancellationToken cancellationToken = default)
    {
        var imprint = await _imprintRepository
            .GetByIdAsync(vehicleSignatureImprintId, cancellationToken)
            .ConfigureAwait(false);

        if (imprint is null)
            return (null, "not_found");

        var storagePath = await ResolveStoragePathAsync(imprint, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(storagePath))
            return (null, "file_missing");

        var preview = await _storage
            .GetPresignedViewUrlAsync(storagePath, cancellationToken)
            .ConfigureAwait(false);

        if (preview is null)
            return (null, "storage_unavailable");

        return (new AttachmentPreviewUrlResult(preview.Value.Url, preview.Value.ExpiresAt), null);
    }

    private async Task<string?> ResolveStoragePathAsync(
        Domain.Entities.VehicleSignatureImprint imprint,
        CancellationToken cancellationToken)
    {
        // Snapshot del PDF firmado: sobrevive al soft-delete / reemplazo del adjunto.
        if (!string.IsNullOrWhiteSpace(imprint.SignedStoragePath))
            return imprint.SignedStoragePath.Trim();

        if (imprint.AttachmentId is not Guid attachmentId)
            return null;

        var instance = await _instanceRepository
            .GetByIdWithAttachmentsAsync(imprint.ProcedureInstanceId, imprint.TenantId, cancellationToken)
            .ConfigureAwait(false);

        var attachment = instance?.Attachments.FirstOrDefault(a => a.Id == attachmentId);
        return string.IsNullOrWhiteSpace(attachment?.StoragePath)
            ? null
            : attachment.StoragePath.Trim();
    }
}
