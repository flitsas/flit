using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Flit.DataMigration.V1.Storage;

/// <summary>Config de un file-manager (origen o destino). Uno por ambiente.</summary>
public sealed class FileManagerEndpoint
{
    /// <summary>URL base del gateway (debe poder terminar en '/'; se normaliza).</summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>Recurso de archivos relativo al base (sin '/' inicial). Igual en V1 y V2.</summary>
    public string FilesPath { get; set; } = "api/v1/files";

    /// <summary>Token Bearer opcional. Hoy los file-managers son públicos; queda por si se activa.</summary>
    public string AuthToken { get; set; } = string.Empty;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(BaseUrl);
}

/// <summary>Un objeto tal como lo describe el file-manager (metadata + URL de descarga).</summary>
public sealed record FileManagerObject(string Id, string Filename, string? Sha256, string DownloadUrl);

/// <summary>Resultado de subir un binario: id nuevo en el file-manager destino + integridad.</summary>
public sealed record UploadResult(string Id, string Sha256, long SizeBytes);

/// <summary>
/// Cliente delgado del file-manager de la empresa, para el migrador de adjuntos. Espeja EXACTAMENTE
/// el contrato que usa la app (<c>FileManagerAttachmentStorage</c>), pero con dos diferencias
/// deliberadas: (1) se instancia DOS veces —origen y destino— con <c>BaseUrl</c> distinto, cosa que
/// el registro DI de la app no permite; (2) al leer, aprovecha que la respuesta de
/// <c>presigned-url</c> ya trae <c>filename</c> y <c>metadata.sha256</c>, así evita una llamada.
/// <para>
/// El <c>sha256</c> se calcula idéntico a la app (<c>Convert.ToHexStringLower(SHA256.HashData)</c>),
/// para que un adjunto migrado sea indistinguible de uno nativo.
/// </para>
/// </summary>
public sealed class FileManagerClient(HttpClient http, string filesPath, string? authToken)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>Calcula el sha256 hex minúsculas de unos bytes, igual que la app de V2.</summary>
    public static string Sha256Hex(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    /// <summary>
    /// Describe un objeto sin descargarlo: <c>GET {files}/{id}/presigned-url</c> devuelve en una
    /// sola llamada la metadata (filename, sha256) y la URL de descarga firmada. Devuelve
    /// <c>null</c> si el file-manager no conoce el id (404). Lanza en otros errores (p. ej. 500 de
    /// un ambiente apagado) para que el llamador lo reporte por adjunto.
    /// </summary>
    public async Task<FileManagerObject?> HeadAsync(string id, CancellationToken ct)
    {
        var path = $"{filesPath}/{Uri.EscapeDataString(id)}/presigned-url";
        using var req = new HttpRequestMessage(HttpMethod.Get, path);
        ApplyAuth(req);
        using var resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        if (resp.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        resp.EnsureSuccessStatusCode();

        using var stream = await resp.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        var root = doc.RootElement;

        var url = root.TryGetProperty("presignedUrl", out var ps) && ps.TryGetProperty("url", out var u)
            ? u.GetString()
            : null;
        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        var filename = root.TryGetProperty("filename", out var fn) ? fn.GetString() : null;
        string? sha256 = null;
        if (root.TryGetProperty("metadata", out var meta) && meta.ValueKind == JsonValueKind.Object
            && meta.TryGetProperty("sha256", out var sh))
        {
            sha256 = sh.GetString();
        }

        return new FileManagerObject(id, filename ?? "file", sha256, url);
    }

    /// <summary>Descarga los bytes desde una URL firmada de S3 (sin auth del file-manager).</summary>
    public async Task<byte[]> DownloadAsync(string signedUrl, CancellationToken ct) =>
        await http.GetByteArrayAsync(signedUrl, ct);

    /// <summary>
    /// Sube unos bytes al file-manager destino: <c>POST {files}</c> crea el registro + presigned, y
    /// luego sube el binario al storage con el método que el file-manager DECIDE en
    /// <c>presignedUrl.method</c> (ADR-0057): <c>PUT</c> con los bytes crudos, o <c>POST</c>
    /// multipart con la POST policy (campos firmados primero, <c>file</c> al final) si viene
    /// ausente, que es lo que hacían todos los backends antes. Devuelve el id NUEVO, que será el
    /// <c>storage_path</c> en V2.
    /// <para>
    /// PUT existe porque el gateway de Contabo (Kong) rechaza el POST policy: sus credenciales
    /// viajan dentro del multipart y el gateway no las ve, así que responde 403 con cuerpo JSON.
    /// Es el mismo criterio que <c>FileManagerAttachmentStorage</c> de la app; este cliente lo
    /// espeja y no deduce nada del proveedor, que no conoce.
    /// </para>
    /// </summary>
    public async Task<UploadResult> UploadAsync(
        Guid procedureInstanceId,
        string tipo,
        string filename,
        byte[] bytes,
        string sha256,
        CancellationToken ct)
    {
        var safeName = string.IsNullOrWhiteSpace(filename) ? "file" : filename;
        var createBody = JsonSerializer.Serialize(
            new
            {
                category = "tramites",
                filename = safeName,
                tags = new[] { tipo },
                metadata = new Dictionary<string, string>
                {
                    ["procedureInstanceId"] = procedureInstanceId.ToString("D"),
                    ["tipo"] = tipo,
                    ["sha256"] = sha256,
                    ["origen"] = "migracion-v1",
                },
            },
            JsonOptions);

        using var createMsg = new HttpRequestMessage(HttpMethod.Post, filesPath)
        {
            Content = new StringContent(createBody, Encoding.UTF8, "application/json"),
        };
        ApplyAuth(createMsg);
        using var createResp = await http.SendAsync(createMsg, ct);
        createResp.EnsureSuccessStatusCode();

        using var createStream = await createResp.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(createStream, cancellationToken: ct);
        var root = doc.RootElement;

        var newId = root.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
        if (string.IsNullOrWhiteSpace(newId)
            || !root.TryGetProperty("presignedUrl", out var ps)
            || !ps.TryGetProperty("url", out var urlEl)
            || urlEl.GetString() is not { Length: > 0 } uploadUrl)
        {
            throw new InvalidOperationException("file-manager destino: respuesta de creación inválida (sin id/presignedUrl).");
        }

        var usePut = ps.TryGetProperty("method", out var methodEl)
            && string.Equals(methodEl.GetString(), "PUT", StringComparison.OrdinalIgnoreCase);

        // URL absoluta del storage ⇒ ignora el BaseAddress del cliente. SIN auth del
        // file-manager: la firma va en la URL (PUT) o en el cuerpo (POST policy).
        using HttpContent content = usePut
            ? BuildPutContent(bytes)
            : BuildPostContent(ps, bytes, safeName);
        using var uploadResp = usePut
            ? await http.PutAsync(uploadUrl, content, ct)
            : await http.PostAsync(uploadUrl, content, ct);
        uploadResp.EnsureSuccessStatusCode();

        return new UploadResult(newId, sha256, bytes.LongLength);
    }

    private static ByteArrayContent BuildPutContent(byte[] bytes)
    {
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        return content;
    }

    private static MultipartFormDataContent BuildPostContent(JsonElement presigned, byte[] bytes, string filename)
    {
        var form = new MultipartFormDataContent();
        // S3 POST policy: los campos firmados (key, policy, x-amz-*) van ANTES del 'file'.
        if (presigned.TryGetProperty("fields", out var fields) && fields.ValueKind == JsonValueKind.Object)
        {
            foreach (var field in fields.EnumerateObject())
            {
                form.Add(new StringContent(field.Value.GetString() ?? string.Empty), field.Name);
            }
        }

        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(fileContent, "file", filename);
        return form;
    }

    private void ApplyAuth(HttpRequestMessage req)
    {
        if (!string.IsNullOrWhiteSpace(authToken))
        {
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", authToken);
        }
    }

    /// <summary>Infiere el mimetype a partir de la extensión del nombre de archivo.</summary>
    public static string GuessMimetype(string filename)
    {
        var ext = Path.GetExtension(filename).ToLower(CultureInfo.InvariantCulture);
        return ext switch
        {
            ".pdf" => "application/pdf",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".webp" => "image/webp",
            ".gif" => "image/gif",
            ".tif" or ".tiff" => "image/tiff",
            _ => "application/octet-stream",
        };
    }
}
