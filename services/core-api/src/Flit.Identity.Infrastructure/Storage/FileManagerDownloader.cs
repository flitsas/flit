using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;

namespace Flit.Infrastructure.Storage;

/// <summary>
/// Descarga de un archivo del file-manager de la empresa: pide el presigned de descarga
/// (<c>GET /files/{id}/presigned-url</c>) y baja los bytes de S3. Epic #13217 (HU #13232): la comparten el
/// almacenamiento de adjuntos de core-api y el lector de logos de core-identity, para no repetir el contrato.
/// </summary>
internal static class FileManagerDownloader
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>El archivo como stream de solo lectura y seekable, o <c>null</c> si no existe.</summary>
    public static async Task<Stream?> OpenReadAsync(
        HttpClient http, FileManagerOptions options, string storagePath, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(storagePath))
            return null;

        var path = $"{options.FilesPath}/{Uri.EscapeDataString(storagePath)}/presigned-url";
        using var req = new HttpRequestMessage(HttpMethod.Get, path);
        ApplyAuth(req, options);
        using var resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        if (resp.StatusCode == HttpStatusCode.NotFound)
            return null;
        resp.EnsureSuccessStatusCode();

        var body = await resp.Content.ReadFromJsonAsync<FilePresignedResponse>(JsonOptions, ct).ConfigureAwait(false);
        var url = body?.PresignedUrl?.Url;
        if (string.IsNullOrWhiteSpace(url))
            return null;

        // Descarga desde S3 y bufferiza para devolver un stream seekable, desligado de la conexión.
        var data = await http.GetByteArrayAsync(new Uri(url), ct).ConfigureAwait(false);
        return new MemoryStream(data, writable: false);
    }

    public static void ApplyAuth(HttpRequestMessage req, FileManagerOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.AuthToken))
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.AuthToken);
    }

    /// <summary>
    /// Opciones del file-manager: configuración primero (<c>FileManager:*</c>), y las variables crudas
    /// <c>FILE_MANAGER_*</c> de respaldo (mismo patrón que Verifik/Kyverum).
    /// </summary>
    public static void Configure(FileManagerOptions o, IConfiguration configuration)
    {
        string? Cfg(string key, string env) =>
            configuration[key] ?? Environment.GetEnvironmentVariable(env);

        o.BaseUrl = Cfg("FileManager:BaseUrl", "FILE_MANAGER_BASE_URL") ?? o.BaseUrl;
        o.FilesPath = Cfg("FileManager:FilesPath", "FILE_MANAGER_FILES_PATH") ?? o.FilesPath;
        o.Category = Cfg("FileManager:Category", "FILE_MANAGER_CATEGORY") ?? o.Category;
        o.TimeoutSeconds = int.TryParse(Cfg("FileManager:TimeoutSeconds", "FILE_MANAGER_TIMEOUT_SECONDS"), out var t)
            ? t : o.TimeoutSeconds;
        o.AuthToken = Cfg("FileManager:AuthToken", "FILE_MANAGER_AUTH_TOKEN") ?? o.AuthToken;
    }

    /// <summary>BaseAddress (con barra final) y timeout del cliente tipado. Las descargas de S3 usan la URL absoluta.</summary>
    public static void ConfigureClient(HttpClient client, FileManagerOptions o, string purpose)
    {
        if (string.IsNullOrWhiteSpace(o.BaseUrl))
            throw new InvalidOperationException(
                $"FileManager:BaseUrl (o FILE_MANAGER_BASE_URL) es obligatoria para {purpose}.");
        var baseUrl = o.BaseUrl.EndsWith('/') ? o.BaseUrl : o.BaseUrl + "/";
        client.BaseAddress = new Uri(baseUrl);
        client.Timeout = TimeSpan.FromSeconds(o.TimeoutSeconds);
    }

    private sealed record FilePresignedResponse(
        [property: JsonPropertyName("presignedUrl")] PresignedUrl? PresignedUrl);

    private sealed record PresignedUrl([property: JsonPropertyName("url")] string? Url);
}
