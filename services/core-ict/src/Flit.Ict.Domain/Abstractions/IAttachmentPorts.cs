using Flit.Ict.Domain.Entities;

namespace Flit.Ict.Domain.Abstractions;

/// <summary>Datos de una carga presignada del File Manager (el cliente sube el binario directo a S3).</summary>
public sealed record PresignedUpload(string UploadUrl, string StoragePath, IReadOnlyDictionary<string, string> Fields);

/// <summary>
/// Almacenamiento de adjuntos vía File Manager corporativo (presign -> S3 -> register). Reusa la
/// misma abstracción que core-api; core-ict apunta al mismo File Manager (FILE_MANAGER_BASE_URL).
/// </summary>
public interface IIctAttachmentStorage
{
    Task<PresignedUpload> CreatePresignedUploadAsync(
        string filename,
        string mimeType,
        CancellationToken ct = default);

    /// <summary>
    /// Sube el contenido del archivo del lado del servidor (flujo v1: el cliente envía los bytes en
    /// multipart) y devuelve el storage_path. En dev sin File Manager, sintetiza el path sin persistir.
    /// </summary>
    Task<string> UploadAsync(
        string filename,
        string mimeType,
        ReadOnlyMemory<byte> content,
        CancellationToken ct = default);
}

/// <summary>Persistencia de adjuntos y catálogos documentales del pre-trámite (RLS por tenant).</summary>
public interface IAttachmentRepository
{
    Task AddAsync(TransactionAttachment attachment, Guid tenantId, CancellationToken ct = default);

    Task<IReadOnlyList<TransactionAttachment>> ListAsync(Guid masterId, Guid tenantId, CancellationToken ct = default);

    /// <summary>Ids de documentos obligatorios faltantes para el tipo de trámite dado.</summary>
    Task<IReadOnlyList<string>> MissingMandatoryDocumentsAsync(
        Guid masterId,
        Guid tenantId,
        short transactionType,
        CancellationToken ct = default);
}
