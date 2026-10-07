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

    /// <summary>
    /// HU #13379 (épica #13216, M2/L1) — como <see cref="OpenReadAsync"/>, pero <b>sin bufferizar</b>: devuelve el cuerpo
    /// de S3 tal como llega (no seekable), atado a la respuesta HTTP, que se libera al disponer el stream. Es para objetos
    /// grandes (partes de lote de hasta cientos de MB) que se reenvían en streaming; <see cref="OpenReadAsync"/> queda
    /// igual para sus consumidores actuales (adjuntos, logos), que necesitan un stream seekable y pequeño.
    /// <c>Position</c> informa los bytes leídos. <c>null</c> si el objeto no existe.
    /// </summary>
    public static async Task<Stream?> OpenReadStreamingAsync(
        HttpClient http, FileManagerOptions options, string storagePath, CancellationToken ct)
    {
        var url = await PresignedDescargaAsync(http, options, storagePath, ct).ConfigureAwait(false);
        if (url is null)
            return null;

        using var req = new HttpRequestMessage(HttpMethod.Get, new Uri(url));
        var resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        try
        {
            if (resp.StatusCode == HttpStatusCode.NotFound)
            {
                resp.Dispose();
                return null;
            }

            resp.EnsureSuccessStatusCode();
            var cuerpo = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            return new RespuestaStream(resp, cuerpo, resp.Content.Headers.ContentLength);
        }
        catch
        {
            resp.Dispose();
            throw;
        }
    }

    private static async Task<string?> PresignedDescargaAsync(
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
        return string.IsNullOrWhiteSpace(url) ? null : url;
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

    /// <summary>Cuerpo de S3 de solo lectura y no seekable que libera la respuesta HTTP al disponerse.</summary>
    private sealed class RespuestaStream(HttpResponseMessage respuesta, Stream cuerpo, long? longitud) : Stream
    {
        private long _leidos;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => longitud ?? throw new NotSupportedException();

        /// <summary>Bytes leídos hasta ahora (no admite asignación).</summary>
        public override long Position
        {
            get => _leidos;
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) => Contar(cuerpo.Read(buffer, offset, count));

        public override int Read(Span<byte> buffer) => Contar(cuerpo.Read(buffer));

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            Contar(await cuerpo.ReadAsync(buffer.AsMemory(offset, count), cancellationToken).ConfigureAwait(false));

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            Contar(await cuerpo.ReadAsync(buffer, cancellationToken).ConfigureAwait(false));

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                cuerpo.Dispose();
                respuesta.Dispose();
            }

            base.Dispose(disposing);
        }

        public override async ValueTask DisposeAsync()
        {
            await cuerpo.DisposeAsync().ConfigureAwait(false);
            respuesta.Dispose();
            await base.DisposeAsync().ConfigureAwait(false);
        }

        private int Contar(int n)
        {
            _leidos += n;
            return n;
        }
    }
}
