using Flit.Ict.Domain.Abstractions;
using Flit.Ict.Domain.Entities;

namespace Flit.Ict.Application.Attachments;

/// <summary>Solicitud de registro de metadata de un adjunto ya subido al File Manager.</summary>
public sealed record RegisterAttachmentInput(
    int IdAttachment,
    string Filename,
    string MimeType,
    long SizeBytes,
    string Sha256,
    string StoragePath);

public sealed record AttachmentView(Guid Id, int IdAttachment, string Filename, string MimeType, long SizeBytes);

/// <summary>Genera una carga presignada del File Manager (el cliente sube el binario directo a S3).</summary>
public sealed class PresignAttachmentHandler(IIctAttachmentStorage storage)
{
    private static readonly HashSet<string> AllowedMimeTypes =
        new(StringComparer.OrdinalIgnoreCase) { "application/pdf", "image/jpeg", "image/png", "image/webp" };

    public async Task<(PresignedUpload? Result, string? Error)> HandleAsync(
        string filename,
        string mimeType,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(filename) || !AllowedMimeTypes.Contains(mimeType))
        {
            return (null, "invalid_file");
        }

        var upload = await storage.CreatePresignedUploadAsync(filename, mimeType, ct);
        return (upload, null);
    }
}

/// <summary>Registra la metadata de un adjunto. Bloquea si el pre-trámite está cerrado o materializado.</summary>
public sealed class RegisterAttachmentHandler(
    IPreTramiteRepository preTramites,
    IAttachmentRepository attachments,
    ICurrentTenant currentTenant)
{
    public async Task<(AttachmentView? Result, string? Error)> HandleAsync(
        Guid masterId,
        RegisterAttachmentInput input,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        var tenantId = currentTenant.TenantId;
        if (tenantId is null)
        {
            return (null, "unauthenticated");
        }

        var master = await preTramites.GetAsync(masterId, tenantId.Value, ct);
        if (master is null)
        {
            return (null, "not_found");
        }

        if (master.ProcedureInstanceId is not null)
        {
            return (null, "already_materialized");
        }

        if (master.ClosedDocument)
        {
            return (null, "closed_document");
        }

        var attachment = new TransactionAttachment
        {
            MasterId = masterId,
            TenantId = tenantId.Value,
            IdAttachment = input.IdAttachment,
            Filename = input.Filename,
            MimeType = input.MimeType,
            SizeBytes = input.SizeBytes,
            Sha256 = input.Sha256,
            StoragePath = input.StoragePath,
            CreatedBy = currentTenant.IntegrationClientId,
        };

        await attachments.AddAsync(attachment, tenantId.Value, ct);
        return (new AttachmentView(attachment.Id, attachment.IdAttachment, attachment.Filename, attachment.MimeType, attachment.SizeBytes), null);
    }
}

/// <summary>Lista los adjuntos de un pre-trámite del tenant.</summary>
public sealed class ListAttachmentsHandler(IAttachmentRepository attachments, ICurrentTenant currentTenant)
{
    public async Task<(IReadOnlyList<AttachmentView>? Result, string? Error)> HandleAsync(
        Guid masterId,
        CancellationToken ct = default)
    {
        var tenantId = currentTenant.TenantId;
        if (tenantId is null)
        {
            return (null, "unauthenticated");
        }

        var list = await attachments.ListAsync(masterId, tenantId.Value, ct);
        var views = list
            .Select(a => new AttachmentView(a.Id, a.IdAttachment, a.Filename, a.MimeType, a.SizeBytes))
            .ToList();
        return (views, null);
    }
}
