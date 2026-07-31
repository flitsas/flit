using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Flit.Ict.Domain.Abstractions;
using Microsoft.Extensions.Options;

namespace Flit.Ict.Infrastructure.Storage;

/// <summary>
/// Opciones del File Manager corporativo. Es el MISMO servicio y contrato que usa core-api
/// (BackCrudFileManager · <c>api/v1/files</c>); core-ict apunta a la MISMA URL
/// (<c>FILE_MANAGER_BASE_URL</c>) para que el adjunto transferido por referencia al borrador
/// abra desde core-api (mismo <c>id</c> del File Manager, mismo bucket). Si difieren, ICT sube a
/// un sitio y core-api lee de otro: el adjunto existe en la tabla pero no abre.
/// </summary>
public sealed class FileManagerOptions
{
    public const string SectionName = "FileManager";

    /// <summary>URL base del File Manager (se normaliza para terminar en '/').</summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>Path del recurso de archivos relativo a <see cref="BaseUrl"/> (sin '/' inicial).</summary>
    public string FilesPath { get; set; } = "api/v1/files";

    /// <summary>Categoría (prefijo de carpeta en S3) con la que se crean los adjuntos de ICT.</summary>
    public string Category { get; set; } = "ict";

    /// <summary>Timeout HTTP en segundos para las llamadas al File Manager y a S3.</summary>
    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Token Bearer opcional. Hoy el File Manager es público (sin auth); si se reactiva, se setea
    /// aquí (o por env <c>FILE_MANAGER_AUTH_TOKEN</c>) y se envía SOLO a la API del File Manager,
    /// nunca a las presigned URLs de S3.
    /// </summary>
    public string AuthToken { get; set; } = string.Empty;
}

/// <summary>
/// Almacenamiento de adjuntos sobre el File Manager corporativo (S3 vía presigned URLs), con el MISMO
/// contrato que core-api:
/// <list type="bullet">
///   <item><c>POST {FilesPath}</c> crea el registro y devuelve <c>{ id, presignedUrl:{ url, fields } }</c>.</item>
///   <item>Los bytes se suben DIRECTO a S3 con la presigned POST policy (campos firmados + 'file').</item>
///   <item>El <c>storage_path</c> persistido por ICT es el <c>id</c> del File Manager — el MISMO handle
///   que core-api usa para abrir el adjunto tras la materialización por referencia.</item>
/// </list>
/// El contrato EXTERNO con el cliente (multipart v1: <c>transactionFlit</c>/<c>idAttachment</c>/
/// <c>closed</c>/<c>file</c>) NO cambia: esto es únicamente la plomería interna hacia el File Manager.
/// </summary>
public sealed class FileManagerAttachmentStorage(HttpClient http, IOptions<FileManagerOptions> options)
    : IIctAttachmentStorage
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly FileManagerOptions _options = options.Value;

    public async Task<PresignedUpload> CreatePresignedUploadAsync(
        string filename,
        string mimeType,
        CancellationToken ct = default)
    {
        // Flujo v2: el cliente sube el binario DIRECTO a S3 con la presigned POST policy. Se devuelve el
        // id del File Manager como storage_path (se persiste al registrar la metadata).
        var created = await CreateFileAsync(filename, mimeType, sha256: null, ct);
        return new PresignedUpload(
            created.PresignedUrl!.Url!,
            created.Id!,
            created.PresignedUrl.Fields ?? new Dictionary<string, string>());
    }

    public async Task<string> UploadAsync(
        string filename,
        string mimeType,
        ReadOnlyMemory<byte> content,
        CancellationToken ct = default)
    {
        // Flujo v1: el cliente envía los bytes en multipart; se suben del lado del servidor al File
        // Manager (crear registro → S3) y se devuelve el id como storage_path.
        var sha256 = Convert.ToHexStringLower(SHA256.HashData(content.Span));
        var created = await CreateFileAsync(filename, mimeType, sha256, ct);
        await UploadToS3Async(created.PresignedUrl!, content.ToArray(), filename, ct);
        return created.Id!;
    }

    /// <summary>Crea el registro en el File Manager (POST {FilesPath}) y obtiene la presigned POST policy.</summary>
    private async Task<CreateFileResponse> CreateFileAsync(
        string filename,
        string mimeType,
        string? sha256,
        CancellationToken ct)
    {
        var name = string.IsNullOrWhiteSpace(filename) ? "file" : filename;
        var metadata = new Dictionary<string, string> { ["contentType"] = mimeType };
        if (!string.IsNullOrWhiteSpace(sha256))
        {
            metadata["sha256"] = sha256;
        }

        var req = new CreateFileRequest(_options.Category, name, [_options.Category], metadata);
        var json = JsonSerializer.Serialize(req, JsonOptions);
        using var msg = new HttpRequestMessage(HttpMethod.Post, _options.FilesPath)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        ApplyAuth(msg);

        using var resp = await http.SendAsync(msg, ct);
        resp.EnsureSuccessStatusCode();

        var created = await resp.Content.ReadFromJsonAsync<CreateFileResponse>(JsonOptions, ct);
        if (created is null
            || string.IsNullOrWhiteSpace(created.Id)
            || string.IsNullOrWhiteSpace(created.PresignedUrl?.Url))
        {
            throw new InvalidOperationException("File Manager: respuesta de creación inválida (sin id/presignedUrl).");
        }

        return created;
    }

    private async Task UploadToS3Async(PresignedUrlDto presigned, byte[] bytes, string filename, CancellationToken ct)
    {
        using var form = new MultipartFormDataContent();
        // S3 POST policy: los campos firmados (key, policy, x-amz-*) van ANTES del 'file'.
        if (presigned.Fields is not null)
        {
            foreach (var (key, value) in presigned.Fields)
            {
                form.Add(new StringContent(value), key);
            }
        }

        form.Add(new ByteArrayContent(bytes), "file", filename);

        // URL absoluta de S3 ⇒ ignora el BaseAddress del cliente. SIN header de auth del File Manager.
        using var resp = await http.PostAsync(presigned.Url, form, ct);
        resp.EnsureSuccessStatusCode();
    }

    private void ApplyAuth(HttpRequestMessage req)
    {
        if (!string.IsNullOrWhiteSpace(_options.AuthToken))
        {
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.AuthToken);
        }
    }

    // ── Contrato del File Manager (BackCrudFileManager · api/v1/files), idéntico al de core-api ──
    private sealed record CreateFileRequest(
        [property: JsonPropertyName("category")] string Category,
        [property: JsonPropertyName("filename")] string Filename,
        [property: JsonPropertyName("tags")] IReadOnlyList<string> Tags,
        [property: JsonPropertyName("metadata")] IReadOnlyDictionary<string, string> Metadata);

    private sealed record CreateFileResponse(
        [property: JsonPropertyName("id")] string? Id,
        [property: JsonPropertyName("presignedUrl")] PresignedUrlDto? PresignedUrl);

    private sealed record PresignedUrlDto(
        [property: JsonPropertyName("url")] string? Url,
        [property: JsonPropertyName("fields")] Dictionary<string, string>? Fields);
}
