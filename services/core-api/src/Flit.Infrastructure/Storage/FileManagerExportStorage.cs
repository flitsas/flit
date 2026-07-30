using Flit.Analytics.Application.Reporting;
using Flit.Tramites.Application.Storage;
using Microsoft.Extensions.Options;

namespace Flit.Infrastructure.Storage;

/// <summary>
/// Adaptador de exportaciones sobre el file-manager (Feature #11076 / ADR-0037).
/// Reutiliza <see cref="IAttachmentStorage"/> con categoría dedicada vía
/// <see cref="ExportFileManagerOptions"/>.
/// </summary>
internal sealed class FileManagerExportStorage(
    IAttachmentStorage attachments,
    IOptions<ExportFileManagerOptions> options) : IExportFileStorage
{
    private readonly ExportFileManagerOptions _options = options.Value;

    public async Task<(string StoragePath, string Sha256, long SizeBytes)> SaveExportAsync(
        Guid jobId,
        string format,
        string fileName,
        Stream content,
        CancellationToken ct = default)
    {
        var tipo = $"export_{format}";
        var stored = await attachments.SaveAsync(jobId, tipo, fileName, content, ct).ConfigureAwait(false);
        return (stored.StoragePath, stored.Sha256, stored.SizeBytes);
    }

    public async Task<(string Url, DateTimeOffset ExpiresAt)?> GetDownloadUrlAsync(
        string storagePath,
        CancellationToken ct = default)
    {
        var result = await attachments.GetPresignedViewUrlAsync(storagePath, ct).ConfigureAwait(false);
        if (result is null) return null;

        // Cap TTL a ≤ 15 min según diseño Feature #11076.
        var maxExpiry = DateTimeOffset.UtcNow.AddMinutes(Math.Clamp(_options.DownloadTtlMinutes, 1, 15));
        var expiresAt = result.Value.ExpiresAt > maxExpiry ? maxExpiry : result.Value.ExpiresAt;
        return (result.Value.Url, expiresAt);
    }
}

public sealed class ExportFileManagerOptions
{
    public const string SectionName = "ExportFileManager";
    public int DownloadTtlMinutes { get; set; } = 15;
}
