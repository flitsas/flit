using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Flit.Comparendos.Api.Persistence;
using Flit.Comparendos.Api.Persistence.Modelo;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Flit.Comparendos.Api.Infraestructura.Archivos;

/// <summary>Un archivo guardado: la referencia que se persiste (id del file-manager o de la base), su huella y tamaño.</summary>
public sealed record ArchivoGuardado(string Referencia, string Sha256, long Tamano);

/// <summary>
/// Archivos del producto: originales del OCR, PDF de los DP, respuestas del OT, comprobantes de pago, documentos del
/// conductor y firmas. Van al file-manager corporativo (MinIO en DEV/QA, Contabo en PDN) en la categoría
/// <c>comparendos</c>, la carpeta hermana de <c>tramites</c> e <c>ict</c>; en local se usa el de DEV, igual que core-api.
/// Guardar en la base (<c>comparendos.archivo_local</c>) solo se permite con <see cref="OpcionesAlmacen.GuardarEnBase"/>
/// (pruebas automáticas): sin URL y sin esa bandera el servicio no arranca, para que un <c>.env</c> incompleto no deje los
/// documentos en la base sin que nadie lo note.
/// </summary>
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
    public string Category { get; set; } = "comparendos";
    public int TimeoutSeconds { get; set; } = 60;
    public string AuthToken { get; set; } = string.Empty;

    /// <summary>Variables <c>FILE_MANAGER_*</c> (las mismas de core-api), con <c>FileManager:*</c> por encima.</summary>
    public static void Configurar(FileManagerOptions o, IConfiguration configuration)
    {
        string? Cfg(string key, string env) => configuration[key] ?? Environment.GetEnvironmentVariable(env);

        o.BaseUrl = Cfg("FileManager:BaseUrl", "FILE_MANAGER_BASE_URL") ?? o.BaseUrl;
        o.FilesPath = Cfg("FileManager:FilesPath", "FILE_MANAGER_FILES_PATH") ?? o.FilesPath;
        o.Category = Cfg("FileManager:Category", "FILE_MANAGER_CATEGORY") ?? o.Category;
        o.TimeoutSeconds = int.TryParse(Cfg("FileManager:TimeoutSeconds", "FILE_MANAGER_TIMEOUT_SECONDS"), out var t) ? t : o.TimeoutSeconds;
        o.AuthToken = Cfg("FileManager:AuthToken", "FILE_MANAGER_AUTH_TOKEN") ?? o.AuthToken;
    }
}

/// <summary>
/// Cliente del file-manager (mismo contrato que <c>FileManagerAttachmentStorage</c> de core-api): POST /files crea el
/// registro y devuelve la URL firmada de subida (POST policy o PUT, lo decide el file-manager); GET
/// /files/{id}/presigned-url da la de descarga. No hay borrado: el file-manager lo limpia solo.
/// </summary>
internal sealed class FileManagerAlmacen(HttpClient http, IOptions<FileManagerOptions> options) : IAlmacenArchivos
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly FileManagerOptions _o = options.Value;

    public async Task<ArchivoGuardado> GuardarAsync(Guid empresaId, string tipo, string nombre, string contentType, byte[] contenido, CancellationToken ct)
    {
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
        return body?.PresignedUrl?.Url is { Length: > 0 } url
            ? await http.GetByteArrayAsync(new Uri(url), ct).ConfigureAwait(false)
            : null;
    }

    private async Task SubirAsync(UrlFirmada firmada, string url, byte[] bytes, string nombre, CancellationToken ct)
    {
        // PUT existe porque hay gateways (Contabo) que rechazan el POST policy. Un reintento ante fallos de red.
        var put = string.Equals(firmada.Method, "PUT", StringComparison.OrdinalIgnoreCase);
        Exception? ultimo = null;
        for (var intento = 0; intento < 2; intento++)
        {
            try
            {
                using HttpContent contenido = put ? Crudo(bytes) : Formulario(firmada, bytes, nombre);
                using var resp = put
                    ? await http.PutAsync(new Uri(url), contenido, ct).ConfigureAwait(false)
                    : await http.PostAsync(new Uri(url), contenido, ct).ConfigureAwait(false);
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

    private static MultipartFormDataContent Formulario(UrlFirmada firmada, byte[] bytes, string nombre)
    {
        var form = new MultipartFormDataContent();
        foreach (var (k, v) in firmada.Fields ?? [])
            form.Add(new StringContent(v), k);
        form.Add(Crudo(bytes), "file", nombre);
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
    /// <summary>Bandera explícita para guardar los archivos en la base. Solo la ponen las pruebas automáticas.</summary>
    public const string GuardarEnBase = "Comparendos:Archivos:GuardarEnBase";
}

/// <summary>
/// File-manager para lo nuevo; lo que ya tenga referencia <c>db:</c> (guardado en la base antes de conectar el
/// file-manager, solo en bases locales) se sigue leyendo de la base.
/// </summary>
internal sealed class AlmacenFileManagerConRespaldo(FileManagerAlmacen fileManager, ComparendosDb db) : IAlmacenArchivos
{
    public Task<ArchivoGuardado> GuardarAsync(Guid empresaId, string tipo, string nombre, string contentType, byte[] contenido, CancellationToken ct) =>
        fileManager.GuardarAsync(empresaId, tipo, nombre, contentType, contenido, ct);

    public Task<byte[]?> LeerAsync(string referencia, CancellationToken ct) =>
        referencia.StartsWith(AlmacenEnBase.Prefijo, StringComparison.Ordinal)
            ? AlmacenEnBase.LeerDeBaseAsync(db, referencia, ct)
            : fileManager.LeerAsync(referencia, ct);
}

/// <summary>Sin file-manager (solo pruebas automáticas): los bytes quedan en <c>comparendos.archivo_local</c>.</summary>
internal sealed class AlmacenEnBase(ComparendosDb db) : IAlmacenArchivos
{
    public async Task<ArchivoGuardado> GuardarAsync(Guid empresaId, string tipo, string nombre, string contentType, byte[] contenido, CancellationToken ct)
    {
        var archivo = new ArchivoLocal
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresaId,
            Tipo = tipo,
            Nombre = nombre,
            ContentType = contentType,
            Contenido = contenido,
            CreadoEn = DateTimeOffset.UtcNow,
        };
        db.ArchivosLocales.Add(archivo);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return new ArchivoGuardado($"{Prefijo}{archivo.Id:D}", Convert.ToHexStringLower(SHA256.HashData(contenido)), contenido.LongLength);
    }

    public Task<byte[]?> LeerAsync(string referencia, CancellationToken ct) => LeerDeBaseAsync(db, referencia, ct);

    public const string Prefijo = "db:";

    public static async Task<byte[]?> LeerDeBaseAsync(ComparendosDb db, string referencia, CancellationToken ct) =>
        referencia.StartsWith(Prefijo, StringComparison.Ordinal) && Guid.TryParse(referencia[Prefijo.Length..], out var id)
            ? await db.ArchivosLocales.Where(a => a.Id == id).Select(a => a.Contenido).FirstOrDefaultAsync(ct).ConfigureAwait(false)
            : null;
}

public static class AlmacenArchivosExtensions
{
    public static IServiceCollection AddAlmacenArchivos(this IServiceCollection services, IConfiguration configuration)
    {
        var opciones = new FileManagerOptions();
        FileManagerOptions.Configurar(opciones, configuration);
        services.Configure<FileManagerOptions>(o => FileManagerOptions.Configurar(o, configuration));
        if (string.IsNullOrWhiteSpace(opciones.BaseUrl))
        {
            if (!configuration.GetValue(OpcionesAlmacen.GuardarEnBase, false))
                throw new InvalidOperationException(
                    "Comparendos no tiene file-manager: define FILE_MANAGER_BASE_URL (la misma URL que core-api; MinIO en DEV/QA, "
                    + "Contabo en PDN, que además exige FILE_MANAGER_AUTH_TOKEN). Guardar los archivos en la base solo se permite "
                    + $"con {OpcionesAlmacen.GuardarEnBase}=true, que es para las pruebas automáticas.");
            services.AddScoped<IAlmacenArchivos, AlmacenEnBase>();
            return services;
        }

        services.AddHttpClient<FileManagerAlmacen>(c =>
        {
            c.BaseAddress = new Uri(opciones.BaseUrl.EndsWith('/') ? opciones.BaseUrl : opciones.BaseUrl + "/");
            c.Timeout = TimeSpan.FromSeconds(opciones.TimeoutSeconds);
        });
        services.AddScoped<IAlmacenArchivos, AlmacenFileManagerConRespaldo>();
        return services;
    }
}
