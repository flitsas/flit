using System.Security.Cryptography;
using Flit.Ict.Domain.Abstractions;
using Flit.Ict.Domain.Entities;

namespace Flit.Ict.Application.Attachments;

/// <summary>Datos del upload v1 (multipart): transactionFlit + filename + idAttachment + closed + bytes.</summary>
public sealed record UploadAttachmentV1Input(
    string TransactionFlit,
    string Filename,
    int IdAttachment,
    bool Closed,
    string MimeType,
    ReadOnlyMemory<byte> Content);

/// <summary>
/// Carga de adjunto compatible con v1 (POST /api/v1/transact-attachments/upload). Resuelve el trámite
/// por su TransactionFlit (manager_id_transaction), valida tamaño (≤ 5.297.000 bytes como v1), almacena
/// el binario y registra la metadata. Si <c>closed</c>, marca el documento cerrado a nuevos adjuntos.
/// </summary>
public sealed class UploadAttachmentV1Handler(
    IPreTramiteRepository preTramites,
    IAttachmentRepository attachments,
    IIctAttachmentStorage storage,
    ICurrentTenant currentTenant)
{
    private const int FileSizeLimit = 5_297_000;

    public async Task<(bool Ok, string? Error)> HandleAsync(UploadAttachmentV1Input input, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        var tenantId = currentTenant.TenantId;
        if (tenantId is null)
        {
            return (false, "unauthenticated");
        }

        if (string.IsNullOrWhiteSpace(input.TransactionFlit) || string.IsNullOrWhiteSpace(input.Filename)
            || input.Content.Length == 0)
        {
            return (false, "invalid_request");
        }

        if (input.Content.Length > FileSizeLimit)
        {
            return (false, "file_too_large");
        }

        var master = await preTramites.FindByManagerIdTransactionAsync(input.TransactionFlit, tenantId.Value, ct);
        if (master is null)
        {
            return (false, "not_found");
        }

        if (master.ProcedureInstanceId is not null)
        {
            return (false, "already_materialized");
        }

        if (master.ClosedDocument)
        {
            return (false, "closed_document");
        }

        var storagePath = await storage.UploadAsync(input.Filename, input.MimeType, input.Content, ct);
        var sha256 = Convert.ToHexStringLower(SHA256.HashData(input.Content.Span));

        var attachment = new TransactionAttachment
        {
            MasterId = master.Id,
            TenantId = tenantId.Value,
            IdAttachment = input.IdAttachment,
            Filename = input.Filename,
            MimeType = input.MimeType,
            SizeBytes = input.Content.Length,
            Sha256 = sha256,
            StoragePath = storagePath,
            CreatedBy = currentTenant.IntegrationClientId,
        };
        await attachments.AddAsync(attachment, tenantId.Value, ct);

        if (input.Closed && !master.ClosedDocument)
        {
            master.ClosedDocument = true;
            master.UpdatedBy = currentTenant.IntegrationClientId;
            await preTramites.SaveAsync(tenantId.Value, ct);
        }

        return (true, null);
    }
}
