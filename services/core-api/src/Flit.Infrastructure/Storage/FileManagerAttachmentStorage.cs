using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Flit.Tramites.Application.Storage;
using Microsoft.Extensions.Options;

namespace Flit.Infrastructure.Storage;

/// <summary>
/// Almacenamiento de adjuntos sobre el file-manager de la empresa (S3 vía presigned URLs).
/// <list type="bullet">
///   <item><c>SaveAsync</c>: POST /files (crea el registro + presigned de subida) y sube los bytes a S3.</item>
///   <item><c>OpenReadAsync</c>: GET /files/{id}/presigned-url y descarga los bytes desde S3.</item>
///   <item><c>Delete</c>: no-op — el file-manager no expone borrado; el objeto queda huérfano y lo
///   recupera su job de limpieza. flit borra la fila en su BD.</item>
/// </list>
/// El <c>StoragePath</c> persistido por flit es el <c>id</c> del file-manager.
/// </summary>
internal sealed class FileManagerAttachmentStorage(
    HttpClient http,
    IOptions<FileManagerOptions> options) : IAttachmentStorage
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly FileManagerOptions _options = options.Value;

    public async Task<StoredFile> SaveAsync(
        Guid procedureInstanceId,
        string tipo,
        string originalFilename,
        Stream content,
        CancellationToken ct = default)
    {
        // 1) Bufferiza el contenido y calcula SHA-256 + size (flit guarda integridad en su BD).
        await using var ms = new MemoryStream();
        await content.CopyToAsync(ms, ct);
        var bytes = ms.ToArray();
        var sha256 = Convert.ToHexStringLower(SHA256.HashData(bytes));
        var filename = string.IsNullOrWhiteSpace(originalFilename) ? "file" : originalFilename;

        // 2) Crea el registro en el file-manager y obtiene la presigned URL de subida.
        var createReq = new CreateFileRequest(
            _options.Category,
            filename,
            [tipo],
            new Dictionary<string, string>
            {
                ["procedureInstanceId"] = procedureInstanceId.ToString("D"),
                ["tipo"] = tipo,
                ["sha256"] = sha256,
            });

        var created = await PostCreateAsync(createReq, ct);
        if (string.IsNullOrWhiteSpace(created?.Id) || string.IsNullOrWhiteSpace(created.PresignedUrl?.Url))
            throw new InvalidOperationException("file-manager: respuesta de creación inválida (sin id/presignedUrl).");

        // 3) Sube los bytes a S3 con el POST policy (campos firmados primero, 'file' al final).
        await UploadToS3Async(created.PresignedUrl, bytes, filename, ct);

        return new StoredFile(created.Id, sha256, bytes.LongLength);
    }

    public async Task<PresignedUpload> CreatePresignedUploadAsync(
        Guid procedureInstanceId,
        string tipo,
        string originalFilename,
        CancellationToken ct = default)
    {
        // Crea el registro en el file-manager (igual que SaveAsync) pero NO sube los bytes: devuelve
        // la presigned POST policy para que el cliente suba directo a S3. El SHA-256 lo calcula el
        // cliente y se persiste al registrar la metadata, por eso aquí no va en metadata.
        var filename = string.IsNullOrWhiteSpace(originalFilename) ? "file" : originalFilename;
        var createReq = new CreateFileRequest(
            _options.Category,
            filename,
            [tipo],
            new Dictionary<string, string>
            {
                ["procedureInstanceId"] = procedureInstanceId.ToString("D"),
                ["tipo"] = tipo,
            });

        var created = await PostCreateAsync(createReq, ct);
        if (string.IsNullOrWhiteSpace(created?.Id) || string.IsNullOrWhiteSpace(created.PresignedUrl?.Url))
            throw new InvalidOperationException("file-manager: respuesta de creación inválida (sin id/presignedUrl).");

        return new PresignedUpload(
            created.Id,
            created.PresignedUrl.Url,
            created.PresignedUrl.Fields ?? new Dictionary<string, string>());
    }

    public async Task<Stream?> OpenReadAsync(string storagePath, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(storagePath))
            return null;

        // GET /files/{id}/presigned-url → presigned de descarga.
        var path = $"{_options.FilesPath}/{Uri.EscapeDataString(storagePath)}/presigned-url";
        using var req = new HttpRequestMessage(HttpMethod.Get, path);
        ApplyAuth(req);
        using var resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        if (resp.StatusCode == HttpStatusCode.NotFound)
            return null;
        resp.EnsureSuccessStatusCode();

        var body = await resp.Content.ReadFromJsonAsync<FilePresignedResponse>(JsonOptions, ct);
        var url = body?.PresignedUrl?.Url;
        if (string.IsNullOrWhiteSpace(url))
            return null;

        // Descarga desde S3 y bufferiza para devolver un stream seekable, desligado de la conexión.
        var data = await http.GetByteArrayAsync(url, ct);
        return new MemoryStream(data, writable: false);
    }

    public async Task<(string Url, DateTimeOffset ExpiresAt)?> GetPresignedViewUrlAsync(
        string storagePath,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(storagePath))
            return null;

        // GET /files/{id}/presigned-url?disposition=inline → presigned de visualización inline.
        // TTL se fija en 10 minutos (ExpiresAt calculado localmente; el file-manager determina el TTL
        // real del objeto firmado en S3). No loguear la URL completa (contiene firma HMAC).
        var path = $"{_options.FilesPath}/{Uri.EscapeDataString(storagePath)}/presigned-url?disposition=inline";
        using var req = new HttpRequestMessage(HttpMethod.Get, path);
        ApplyAuth(req);
        using var resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        if (resp.StatusCode == HttpStatusCode.NotFound)
            return null;
        resp.EnsureSuccessStatusCode();

        var body = await resp.Content.ReadFromJsonAsync<FilePresignedResponse>(JsonOptions, ct);
        var url = body?.PresignedUrl?.Url;
        if (string.IsNullOrWhiteSpace(url))
            return null;

        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(10);
        return (url, expiresAt);
    }

    public void Delete(string storagePath)
    {
        // No-op: el file-manager no expone borrado. El objeto en S3 queda huérfano y lo recupera
        // su job de limpieza (cold storage a los 30 días). flit elimina la fila en su BD.
        _ = storagePath;
    }

    private async Task<CreateFileResponse?> PostCreateAsync(CreateFileRequest req, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(req, JsonOptions);
        using var msg = new HttpRequestMessage(HttpMethod.Post, _options.FilesPath)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        ApplyAuth(msg);
        using var resp = await http.SendAsync(msg, ct);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<CreateFileResponse>(JsonOptions, ct);
    }

    private async Task UploadToS3Async(PresignedUrl presigned, byte[] bytes, string filename, CancellationToken ct)
    {
        using var form = new MultipartFormDataContent();
        // S3 POST policy: los campos firmados (key, policy, x-amz-*) van ANTES del 'file'.
        if (presigned.Fields is not null)
            foreach (var (key, value) in presigned.Fields)
                form.Add(new StringContent(value), key);

        form.Add(new ByteArrayContent(bytes), "file", filename);

        // URL absoluta de S3 ⇒ ignora el BaseAddress del cliente. SIN header de auth del file-manager.
        using var resp = await http.PostAsync(presigned.Url, form, ct);
        resp.EnsureSuccessStatusCode();
    }

    private void ApplyAuth(HttpRequestMessage req)
    {
        if (!string.IsNullOrWhiteSpace(_options.AuthToken))
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.AuthToken);
    }

    // ── Contrato del file-manager (BackCrudFileManager · api/v1/files) ──────────────
    private sealed record CreateFileRequest(
        [property: JsonPropertyName("category")] string Category,
        [property: JsonPropertyName("filename")] string Filename,
        [property: JsonPropertyName("tags")] IReadOnlyList<string> Tags,
        [property: JsonPropertyName("metadata")] IReadOnlyDictionary<string, string> Metadata);

    private sealed record CreateFileResponse(
        [property: JsonPropertyName("id")] string? Id,
        [property: JsonPropertyName("presignedUrl")] PresignedUrl? PresignedUrl);

    private sealed record FilePresignedResponse(
        [property: JsonPropertyName("presignedUrl")] PresignedUrl? PresignedUrl);

    private sealed record PresignedUrl(
        [property: JsonPropertyName("url")] string? Url,
        [property: JsonPropertyName("fields")] Dictionary<string, string>? Fields);
}
