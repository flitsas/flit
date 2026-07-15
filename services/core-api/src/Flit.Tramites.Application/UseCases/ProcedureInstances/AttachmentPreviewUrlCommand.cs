using Flit.Tramites.Application.Storage;
using Flit.Tramites.Domain.Repositories;

namespace Flit.Tramites.Application.UseCases.ProcedureInstances;

/// <summary>Respuesta del handler de preview-url: URL firmada + tiempo de expiración.</summary>
public sealed record AttachmentPreviewUrlResult(string Url, DateTimeOffset ExpiresAt);

/// <summary>
/// Resuelve una presigned GET URL con <c>Content-Disposition: inline</c> para visualizar un adjunto
/// en el navegador sin forzar descarga (Feature #10701 / ADR-0029).
/// <para>
/// El ownership se valida contra la instancia + tenant antes de emitir la URL.
/// La URL no se loguea (contiene firma HMAC del file-manager).
/// </para>
/// </summary>
public sealed class GetAttachmentPreviewUrlHandler(
    IProcedureInstanceRepository repo,
    IAttachmentStorage storage)
{
    public async Task<(AttachmentPreviewUrlResult? Result, string? Error)> HandleAsync(
        Guid instanceId,
        Guid tenantId,
        Guid attachmentId,
        CancellationToken ct = default)
    {
        var instance = await repo.GetByIdWithAttachmentsAsync(instanceId, tenantId, ct);
        if (instance is null)
            return (null, "not_found");

        var attachment = instance.Attachments.FirstOrDefault(a => a.Id == attachmentId);
        if (attachment is null)
            return (null, "not_found");

        var result = await storage.GetPresignedViewUrlAsync(attachment.StoragePath, ct);
        if (result is null)
            return (null, "storage_unavailable");

        return (new AttachmentPreviewUrlResult(result.Value.Url, result.Value.ExpiresAt), null);
    }
}
