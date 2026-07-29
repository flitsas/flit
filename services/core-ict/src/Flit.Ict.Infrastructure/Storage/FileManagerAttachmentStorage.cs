using System.Net.Http.Json;
using Flit.Ict.Domain.Abstractions;
using Microsoft.Extensions.Options;

namespace Flit.Ict.Infrastructure.Storage;

/// <summary>Opciones del File Manager corporativo (mismo que core-api).</summary>
public sealed class FileManagerOptions
{
    public const string SectionName = "FileManager";

    public string BaseUrl { get; init; } = string.Empty;
}

/// <summary>
/// Genera cargas presignadas contra el File Manager corporativo (presign -> S3). Si no hay BaseUrl
/// configurada (dev sin File Manager), sintetiza un storage_path para no romper el flujo local.
/// </summary>
public sealed class FileManagerAttachmentStorage(HttpClient http, IOptions<FileManagerOptions> options)
    : IIctAttachmentStorage
{
    private readonly FileManagerOptions _options = options.Value;

    public async Task<PresignedUpload> CreatePresignedUploadAsync(
        string filename,
        string mimeType,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_options.BaseUrl))
        {
            var devPath = $"ict/dev/{Guid.NewGuid():N}/{filename}";
            return new PresignedUpload("about:blank#dev-upload", devPath, new Dictionary<string, string>());
        }

        using var response = await http.PostAsJsonAsync(
            new Uri(new Uri(_options.BaseUrl), "files"),
            new { filename, contentType = mimeType, category = "ict" },
            ct);
        response.EnsureSuccessStatusCode();

        var dto = await response.Content.ReadFromJsonAsync<FileManagerPresignResponse>(ct)
            ?? throw new InvalidOperationException("Respuesta inválida del File Manager.");
        return new PresignedUpload(
            dto.UploadUrl,
            dto.StoragePath,
            dto.Fields ?? new Dictionary<string, string>());
    }

    public async Task<string> UploadAsync(
        string filename,
        string mimeType,
        ReadOnlyMemory<byte> content,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_options.BaseUrl))
        {
            // Dev sin File Manager: no se persiste el binario, solo se sintetiza el path.
            return $"ict/dev/{Guid.NewGuid():N}/{filename}";
        }

        var presign = await CreatePresignedUploadAsync(filename, mimeType, ct);
        using var form = new MultipartFormDataContent();
        foreach (var (key, value) in presign.Fields)
        {
            form.Add(new StringContent(value), key);
        }

        var fileContent = new ByteArrayContent(content.ToArray());
        fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(mimeType);
        form.Add(fileContent, "file", filename);

        using var response = await http.PostAsync(new Uri(presign.UploadUrl), form, ct);
        response.EnsureSuccessStatusCode();
        return presign.StoragePath;
    }

    private sealed record FileManagerPresignResponse(
        string UploadUrl,
        string StoragePath,
        Dictionary<string, string>? Fields);
}
