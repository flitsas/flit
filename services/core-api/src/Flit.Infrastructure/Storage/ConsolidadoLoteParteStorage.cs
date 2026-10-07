using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Flit.Tramites.Application.Storage;
using Flit.Tramites.Application.UseCases.ConsolidadoLotes;
using Microsoft.Extensions.Options;

namespace Flit.Infrastructure.Storage;

/// <summary>
/// Partes cifradas del lote de consolidados sobre el file-manager de la empresa (épica #13216, HU #13372, ADR-0070 §D4).
/// <list type="bullet">
///   <item><c>SubirAsync</c>: calcula SHA-256 y tamaño leyendo el archivo en streaming, crea el registro
///   (<c>POST /files</c>) y sube con <see cref="StreamContent"/> sobre un <see cref="FileStream"/> abierto por intento:
///   <c>PUT</c> con los bytes crudos o <c>POST</c> multipart con los campos firmados primero, según el <c>method</c>
///   que devuelve el file-manager (ADR-0057 D1). Nunca pasa por <c>IAttachmentStorage.SaveAsync</c> (bufferiza en
///   <c>byte[]</c>). El contenido lleva <c>Content-Length</c> (sin chunked), que S3/Ceph exigen en el PUT firmado.</item>
///   <item><c>OpenReadAsync</c>: <see cref="FileManagerDownloader.OpenReadAsync"/> (mismo contrato que los adjuntos).</item>
///   <item><c>Delete</c>: no-op, como <c>FileManagerAttachmentStorage.Delete</c> (V-g).</item>
/// </list>
/// El cliente HTTP se registra sin timeout global: cada llamada al API del file-manager usa
/// <see cref="FileManagerOptions.TimeoutSeconds"/> y la subida suma un segundo por MiB (rendimiento mínimo 1 MiB/s),
/// porque una parte de cientos de MB no cabe en los 30 s por defecto.
/// </summary>
internal sealed class ConsolidadoLoteParteStorage(
    HttpClient http,
    IOptions<FileManagerOptions> options) : IConsolidadoLoteParteStorage
{
    internal const string TagParte = "consolidado_lote_parte";
    internal const int BuferArchivo = 81920;

    /// <summary>Tope de la lectura completa de una parte (hoy <see cref="FileManagerDownloader"/> la baja entera).</summary>
    internal static readonly TimeSpan TiempoLectura = TimeSpan.FromMinutes(15);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly FileManagerOptions _options = options.Value;

    public async Task<StoredFile> SubirAsync(
        Guid loteId, int partNumber, string rutaArchivoCifrado, CancellationToken ct = default)
    {
        if (loteId == Guid.Empty)
            throw new ArgumentException("El id del lote es obligatorio.", nameof(loteId));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(partNumber);
        ArgumentException.ThrowIfNullOrWhiteSpace(rutaArchivoCifrado);

        // 1) Integridad del archivo cifrado en streaming (sin cargarlo en memoria).
        string sha256;
        long tamano;
        await using (var lectura = AbrirArchivo(rutaArchivoCifrado))
        {
            tamano = lectura.Length;
            sha256 = Convert.ToHexStringLower(await SHA256.HashDataAsync(lectura, ct).ConfigureAwait(false));
        }

        // 2) Registro en el file-manager + presigned de subida. Nombre sin PII (solo ids técnicos).
        var filename = NombreArchivo(loteId, partNumber);
        var creado = await CrearRegistroAsync(
            new CreateFileRequest(
                _options.Category,
                filename,
                [TagParte],
                new Dictionary<string, string>
                {
                    ["loteId"] = loteId.ToString("D"),
                    ["partNumber"] = partNumber.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["formato"] = "FLZ1",
                    ["sha256"] = sha256,
                }),
            ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(creado?.Id) || string.IsNullOrWhiteSpace(creado.PresignedUrl?.Url))
            throw new InvalidOperationException("file-manager: respuesta de creación inválida (sin id/presignedUrl).");

        // 3) Subida en streaming al storage.
        await SubirAlStorageAsync(creado.PresignedUrl, rutaArchivoCifrado, filename, tamano, ct).ConfigureAwait(false);
        return new StoredFile(creado.Id, sha256, tamano);
    }

    public async Task<Stream?> OpenReadAsync(string storagePath, CancellationToken ct = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TiempoLectura);
        return await FileManagerDownloader.OpenReadAsync(http, _options, storagePath, cts.Token).ConfigureAwait(false);
    }

    public void Delete(string storagePath)
    {
        // No-op: el file-manager no expone borrado (V-g). Sin la DEK del lote el objeto es ilegible.
        _ = storagePath;
    }

    internal static string NombreArchivo(Guid loteId, int partNumber) =>
        $"lote-{loteId:N}-parte-{partNumber}.flz";

    /// <summary>Tiempo máximo de la subida: el timeout del file-manager más 1 s por MiB.</summary>
    internal TimeSpan TiempoSubida(long tamano) =>
        TimeSpan.FromSeconds(Math.Max(1, _options.TimeoutSeconds) + Math.Ceiling(tamano / (1024d * 1024d)));

    private async Task<CreateFileResponse?> CrearRegistroAsync(CreateFileRequest req, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, _options.TimeoutSeconds)));
        var json = JsonSerializer.Serialize(req, JsonOptions);
        using var msg = new HttpRequestMessage(HttpMethod.Post, _options.FilesPath)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        FileManagerDownloader.ApplyAuth(msg, _options);
        using var resp = await http.SendAsync(msg, cts.Token).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<CreateFileResponse>(JsonOptions, cts.Token).ConfigureAwait(false);
    }

    private async Task SubirAlStorageAsync(
        PresignedUrl presigned, string ruta, string filename, long tamano, CancellationToken ct)
    {
        // El método lo decide el file-manager (ADR-0057 D1). Ausente ⇒ POST.
        var usePut = string.Equals(presigned.Method, "PUT", StringComparison.OrdinalIgnoreCase);

        // Reintento único ante fallos transitorios; cada intento reabre el archivo desde el inicio.
        Exception? last = null;
        for (var attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TiempoSubida(tamano));
                using var content = usePut
                    ? ContenidoPut(AbrirArchivo(ruta))
                    : (HttpContent)ContenidoPost(presigned, AbrirArchivo(ruta), filename);

                // URL absoluta del storage y SIN auth del file-manager: la firma va en la URL o en el cuerpo.
                using var req = new HttpRequestMessage(usePut ? HttpMethod.Put : HttpMethod.Post, presigned.Url)
                {
                    Content = content,
                };
                using var resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cts.Token)
                    .ConfigureAwait(false);
                resp.EnsureSuccessStatusCode();
                return;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                last = ex;
            }
        }

        throw new InvalidOperationException(
            $"file-manager: no se pudo subir la parte del lote al storage ({(usePut ? "PUT" : "POST")}) tras reintentar.",
            last);
    }

    private static FileStream AbrirArchivo(string ruta) =>
        new(ruta, FileMode.Open, FileAccess.Read, FileShare.Read, BuferArchivo,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

    // StreamContent dispone el FileStream al disponerse el contenido.
    private static StreamContent ContenidoPut(FileStream archivo)
    {
        var content = new StreamContent(archivo, BuferArchivo);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        content.Headers.ContentLength = archivo.Length;
        return content;
    }

    private static MultipartFormDataContent ContenidoPost(PresignedUrl presigned, FileStream archivo, string filename)
    {
        var form = new MultipartFormDataContent();
        // S3 POST policy: los campos firmados (key, policy, x-amz-*) van ANTES del 'file'.
        if (presigned.Fields is not null)
            foreach (var (key, value) in presigned.Fields)
                form.Add(new StringContent(value), key);

        form.Add(ContenidoPut(archivo), "file", filename);
        return form;
    }

    // ── Contrato del file-manager (BackCrudFileManager · api/v1/files), igual que FileManagerAttachmentStorage ──
    private sealed record CreateFileRequest(
        [property: JsonPropertyName("category")] string Category,
        [property: JsonPropertyName("filename")] string Filename,
        [property: JsonPropertyName("tags")] IReadOnlyList<string> Tags,
        [property: JsonPropertyName("metadata")] IReadOnlyDictionary<string, string> Metadata);

    private sealed record CreateFileResponse(
        [property: JsonPropertyName("id")] string? Id,
        [property: JsonPropertyName("presignedUrl")] PresignedUrl? PresignedUrl);

    // Method: "POST" (multipart con Fields) o "PUT" (bytes crudos). Ausente ⇒ POST.
    private sealed record PresignedUrl(
        [property: JsonPropertyName("url")] string? Url,
        [property: JsonPropertyName("method")] string? Method,
        [property: JsonPropertyName("fields")] Dictionary<string, string>? Fields);
}
