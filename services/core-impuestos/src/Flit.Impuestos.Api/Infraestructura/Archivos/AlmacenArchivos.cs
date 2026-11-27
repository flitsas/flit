using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace Flit.Impuestos.Api.Infraestructura.Archivos;

// Forma copiada (no referenciada, EP-CF1) del almacén de archivos de core-diagnostico (Infraestructura/Archivos/AlmacenArchivos.cs):
// mismo contrato del file-manager corporativo (ADR-0057). Cambian la categoría (impuestos), la bandera y el respaldo de pruebas: el
// esquema impuestos no tiene tabla de archivos locales (plan §4.1), así que sin file-manager los bytes quedan en memoria del proceso.

/// <summary>Un archivo guardado: la referencia que se persiste (id del file-manager o de la memoria), su huella y su tamaño.</summary>
public sealed record ArchivoGuardado(string Referencia, string Sha256, long Tamano);

/// <summary>
/// Archivos del producto Impuestos (HU #14694 M11, DA-9c): los recibos PDF que el proveedor entrega y la suite sirve a su
/// usuario. Van al file-manager corporativo (MinIO en DEV/QA, Contabo en PDN) en la categoría <c>impuestos</c>. Sin URL del
/// file-manager el servicio no arranca, salvo con <see cref="OpcionesAlmacen.GuardarEnMemoria"/> (pruebas automáticas).
/// </summary>
/// <example>
/// <code>
/// var guardado = await almacen.GuardarAsync(empresa, "recibo", "recibo-0193.pdf", "application/pdf", pdf, ct);
/// var bytes = await almacen.LeerAsync(guardado.Referencia, ct); // null si ya no existe
/// </code>
/// </example>
public interface IAlmacenArchivos
{
    Task<ArchivoGuardado> GuardarAsync(Guid empresaId, string tipo, string nombre, string contentType, byte[] contenido, CancellationToken ct);

    /// <summary>Los bytes del archivo, o null si ya no existe.</summary>
    Task<byte[]?> LeerAsync(string referencia, CancellationToken ct);
}

public sealed class FileManagerOptions
{
    public string BaseUrl { get; set; } = string.Empty;
    public string FilesPath { get; set; } = "api/v1/files";
    public string Category { get; set; } = "impuestos";
    public int TimeoutSeconds { get; set; } = 60;
    public string AuthToken { get; set; } = string.Empty;

    /// <summary>Variables <c>FILE_MANAGER_*</c> (las mismas de core-api), con <c>FileManager:*</c> por encima.</summary>
    public static void Configurar(FileManagerOptions o, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(o);
        ArgumentNullException.ThrowIfNull(configuration);
        string? Cfg(string key, string env) => configuration[key] ?? Environment.GetEnvironmentVariable(env);

        o.BaseUrl = Cfg("FileManager:BaseUrl", "FILE_MANAGER_BASE_URL") ?? o.BaseUrl;
        o.FilesPath = Cfg("FileManager:FilesPath", "FILE_MANAGER_FILES_PATH") ?? o.FilesPath;
        o.Category = Cfg("FileManager:Category", "FILE_MANAGER_CATEGORY") ?? o.Category;
        o.TimeoutSeconds = int.TryParse(Cfg("FileManager:TimeoutSeconds", "FILE_MANAGER_TIMEOUT_SECONDS"), out var t) ? t : o.TimeoutSeconds;
        o.AuthToken = Cfg("FileManager:AuthToken", "FILE_MANAGER_AUTH_TOKEN") ?? o.AuthToken;
    }
}

/// <summary>
/// Cliente del file-manager (mismo contrato que el de core-api): POST /files crea el registro y devuelve la URL firmada de subida
/// (POST policy o PUT, lo decide el file-manager); GET /files/{id}/presigned-url da la de descarga. No hay borrado. El token del
/// file-manager va en <c>Authorization</c>, que el log de <c>HttpClient</c> redacta (EP-CF3a).
/// </summary>
internal sealed class FileManagerAlmacen(HttpClient http, IOptions<FileManagerOptions> options) : IAlmacenArchivos
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly FileManagerOptions _o = options.Value;

    public async Task<ArchivoGuardado> GuardarAsync(Guid empresaId, string tipo, string nombre, string contentType, byte[] contenido, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(contenido);
        var sha = Convert.ToHexStringLower(SHA256.HashData(contenido));
        var archivo = string.IsNullOrWhiteSpace(nombre) ? "archivo" : nombre;
        var pedido = new CrearArchivo(_o.Category, archivo, [tipo], new Dictionary<string, string>
        {
            ["empresaId"] = empresaId.ToString("D"),
            ["tipo"] = tipo,
            ["sha256"] = sha,
        });

        using var msg = new HttpRequestMessage(HttpMethod.Post, _o.FilesPath)
        {
            Content = new StringContent(JsonSerializer.Serialize(pedido, Json), Encoding.UTF8, "application/json"),
        };
        Autenticar(msg);
        using var resp = await http.SendAsync(msg, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        var creado = await resp.Content.ReadFromJsonAsync<ArchivoCreado>(Json, ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(creado?.Id) || creado.PresignedUrl?.Url is not { Length: > 0 } url)
            throw new InvalidOperationException("file-manager: respuesta de creación sin id o sin URL firmada.");

        await SubirAsync(creado.PresignedUrl, url, contenido, archivo, ct).ConfigureAwait(false);
        return new ArchivoGuardado(creado.Id, sha, contenido.LongLength);
    }

    public async Task<byte[]?> LeerAsync(string referencia, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(referencia))
            return null;

        using var req = new HttpRequestMessage(HttpMethod.Get, $"{_o.FilesPath}/{Uri.EscapeDataString(referencia)}/presigned-url");
        Autenticar(req);
        using var resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        if (resp.StatusCode == HttpStatusCode.NotFound)
            return null;
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadFromJsonAsync<UrlFirmadaRespuesta>(Json, ct).ConfigureAwait(false);
        if (body?.PresignedUrl?.Url is not { Length: > 0 } url)
            return null;

        using var archivo = await http.GetAsync(new Uri(url), HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        if (archivo.StatusCode == HttpStatusCode.NotFound)
            return null;
        archivo.EnsureSuccessStatusCode();
        return await archivo.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
    }

    private async Task SubirAsync(UrlFirmada firmada, string url, byte[] contenido, string nombre, CancellationToken ct)
    {
        // PUT existe porque hay gateways (Contabo) que rechazan el POST policy. Un reintento ante fallos de red.
        var put = string.Equals(firmada.Method, "PUT", StringComparison.OrdinalIgnoreCase);
        Exception? ultimo = null;
        for (var intento = 0; intento < 2; intento++)
        {
            try
            {
                using HttpContent cuerpo = put ? Crudo(contenido) : Formulario(firmada, Crudo(contenido), nombre);
                using var resp = put
                    ? await http.PutAsync(new Uri(url), cuerpo, ct).ConfigureAwait(false)
                    : await http.PostAsync(new Uri(url), cuerpo, ct).ConfigureAwait(false);
                resp.EnsureSuccessStatusCode();
                return;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (HttpRequestException ex)
            {
                ultimo = ex;
            }
        }

        throw new InvalidOperationException("file-manager: no se pudo subir el archivo tras reintentar.", ultimo);
    }

    private static ByteArrayContent Crudo(byte[] bytes)
    {
        var c = new ByteArrayContent(bytes);
        c.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        return c;
    }

    private static MultipartFormDataContent Formulario(UrlFirmada firmada, HttpContent archivo, string nombre)
    {
        var form = new MultipartFormDataContent();
        foreach (var (k, v) in firmada.Fields ?? [])
            form.Add(new StringContent(v), k);
        form.Add(archivo, "file", nombre);
        return form;
    }

    private void Autenticar(HttpRequestMessage req)
    {
        if (!string.IsNullOrWhiteSpace(_o.AuthToken))
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _o.AuthToken);
    }

    private sealed record CrearArchivo(
        [property: JsonPropertyName("category")] string Category,
        [property: JsonPropertyName("filename")] string Filename,
        [property: JsonPropertyName("tags")] IReadOnlyList<string> Tags,
        [property: JsonPropertyName("metadata")] IReadOnlyDictionary<string, string> Metadata);

    private sealed record ArchivoCreado(
        [property: JsonPropertyName("id")] string? Id,
        [property: JsonPropertyName("presignedUrl")] UrlFirmada? PresignedUrl);

    private sealed record UrlFirmadaRespuesta([property: JsonPropertyName("presignedUrl")] UrlFirmada? PresignedUrl);

    private sealed record UrlFirmada(
        [property: JsonPropertyName("url")] string? Url,
        [property: JsonPropertyName("method")] string? Method,
        [property: JsonPropertyName("fields")] Dictionary<string, string>? Fields);
}

public static class OpcionesAlmacen
{
    /// <summary>Bandera explícita para guardar los archivos en la memoria del proceso. Solo la ponen las pruebas automáticas.</summary>
    public const string GuardarEnMemoria = "Impuestos:Archivos:GuardarEnMemoria";
}

/// <summary>
/// Sin file-manager (solo pruebas automáticas, <see cref="OpcionesAlmacen.GuardarEnMemoria"/>): los bytes quedan en la memoria del
/// proceso y se pierden al reiniciar. La referencia lleva el prefijo <see cref="Prefijo"/>.
/// </summary>
internal sealed class AlmacenEnMemoria : IAlmacenArchivos
{
    public const string Prefijo = "mem:";

    private readonly ConcurrentDictionary<string, byte[]> _archivos = new(StringComparer.Ordinal);

    public Task<ArchivoGuardado> GuardarAsync(Guid empresaId, string tipo, string nombre, string contentType, byte[] contenido, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(contenido);
        var referencia = $"{Prefijo}{Guid.CreateVersion7():D}";
        _archivos[referencia] = [.. contenido];
        return Task.FromResult(new ArchivoGuardado(referencia, Convert.ToHexStringLower(SHA256.HashData(contenido)), contenido.LongLength));
    }

    public Task<byte[]?> LeerAsync(string referencia, CancellationToken ct) =>
        Task.FromResult(referencia is not null && _archivos.TryGetValue(referencia, out var bytes) ? (byte[]?)[.. bytes] : null);
}

public static class AlmacenArchivosExtensions
{
    /// <summary>
    /// File-manager con <c>FILE_MANAGER_BASE_URL</c> (o <c>FileManager:BaseUrl</c>); sin URL, la memoria solo con
    /// <see cref="OpcionesAlmacen.GuardarEnMemoria"/>=true; sin ninguna de las dos, el servicio no arranca (fail-closed: un
    /// <c>.env</c> incompleto no deja los recibos del cliente en un lugar que se pierde).
    /// </summary>
    public static IServiceCollection AddAlmacenArchivos(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var opciones = new FileManagerOptions();
        FileManagerOptions.Configurar(opciones, configuration);
        services.Configure<FileManagerOptions>(o => FileManagerOptions.Configurar(o, configuration));
        if (string.IsNullOrWhiteSpace(opciones.BaseUrl))
        {
            if (!configuration.GetValue(OpcionesAlmacen.GuardarEnMemoria, false))
                throw new InvalidOperationException(
                    "Impuestos no tiene file-manager: define FILE_MANAGER_BASE_URL (la misma URL que core-api; MinIO en DEV/QA, "
                    + "Contabo en PDN, que además exige FILE_MANAGER_AUTH_TOKEN) y FILE_MANAGER_CATEGORY=impuestos. Guardar los "
                    + $"recibos en memoria solo se permite con {OpcionesAlmacen.GuardarEnMemoria.Replace(":", "__", StringComparison.Ordinal)}=true, "
                    + "que es para las pruebas automáticas.");
            services.AddSingleton<IAlmacenArchivos, AlmacenEnMemoria>();
            return services;
        }

        services.AddHttpClient<FileManagerAlmacen>(c =>
        {
            c.BaseAddress = new Uri(opciones.BaseUrl.EndsWith('/') ? opciones.BaseUrl : opciones.BaseUrl + "/");
            c.Timeout = TimeSpan.FromSeconds(opciones.TimeoutSeconds);
        });
        services.AddScoped<IAlmacenArchivos>(sp => sp.GetRequiredService<FileManagerAlmacen>());
        return services;
    }
}
