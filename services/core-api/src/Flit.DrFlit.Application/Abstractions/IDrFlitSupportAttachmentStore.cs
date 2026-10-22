using Flit.DrFlit.Application.SupportCases;

namespace Flit.DrFlit.Application.Abstractions;

/// <summary>
/// Adjuntos que el usuario sube ANTES de confirmar un caso de soporte (HU #12924, ADR-0060 §5.3). Nacen
/// sin caso y con vencimiento; al confirmar se vinculan (HU #12925). La implementación guarda el binario
/// en el almacenamiento de adjuntos y la metadata en <c>dr_flit.support_case_attachments</c>.
/// </summary>
public interface IDrFlitSupportAttachmentStore
{
    Task<DrFlitStoredAttachment> SaveAsync(
        Guid tenantId,
        Guid userId,
        string fileName,
        string contentType,
        Stream content,
        DateTimeOffset expiresAt,
        CancellationToken ct);

    /// <summary>Borra (fila y binario) los adjuntos de este usuario que vencieron sin confirmarse.</summary>
    Task<int> PurgeExpiredAsync(Guid tenantId, Guid userId, DateTimeOffset now, CancellationToken ct);

    /// <summary>
    /// Adjuntos pendientes (sin caso, no vencidos) de este usuario y tenant entre <paramref name="ids"/>.
    /// Los que no cumplen se omiten: nadie puede adjuntar el archivo de otro adivinando su id.
    /// </summary>
    Task<IReadOnlyList<DrFlitPendingAttachment>> GetPendingAsync(
        Guid tenantId, Guid userId, IReadOnlyCollection<Guid> ids, DateTimeOffset now, CancellationToken ct);

    /// <summary>Vincula los adjuntos al caso para que no se purguen.</summary>
    Task LinkToCaseAsync(IReadOnlyCollection<Guid> attachmentIds, Guid supportCaseId, CancellationToken ct);

    /// <summary>Abre el binario guardado; <c>null</c> si ya no existe.</summary>
    Task<Stream?> OpenAsync(string storagePath, CancellationToken ct);
}

/// <summary>Adjunto recién guardado, tal como lo ve el cliente.</summary>
public sealed record DrFlitStoredAttachment(Guid Id, string FileName, long SizeBytes);

/// <summary>Adjunto pendiente listo para enviarse con el caso.</summary>
public sealed record DrFlitPendingAttachment(Guid Id, string FileName, string ContentType, string StoragePath);

/// <summary>
/// Límites y ambiente de los casos de soporte (<c>DrFlit:SupportCase:*</c> y <c>DrFlit:DeployEnvironment</c>).
/// Todo configurable: los valores del diseño son el default, no un tope fijo.
/// </summary>
public interface IDrFlitSupportCaseSettings
{
    int MaxAttachments { get; }

    long MaxFileSizeBytes { get; }

    IReadOnlyCollection<string> AllowedMimeTypes { get; }

    TimeSpan AttachmentTtl { get; }

    /// <summary>Ambiente que se reporta en el caso. Default DEV si no está configurado.</summary>
    DrFlitDeployEnvironment DeployEnvironment { get; }
}
