using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Flit.Diagnostico.Api.Modulos.Comun;
using Flit.Diagnostico.Api.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Flit.Diagnostico.Api.Infraestructura.Archivos;

// Copiado de services/core-comparendos/src/Flit.Comparendos.Api/Infraestructura/Archivos/AlmacenArchivos.cs (HU #13846): mismo
// contrato del file-manager corporativo; cambian la categoría (diagnostico), la bandera y la tabla de respaldo
// (diagnostico.archivo_local, con la empresa en tenant_id y el filtro global de ADR-0072).

/// <summary>Un archivo guardado: la referencia que se persiste (id del file-manager o de la base), su huella y tamaño.</summary>
public sealed record ArchivoGuardado(string Referencia, string Sha256, long Tamano);

/// <summary>
/// Archivos del producto Diagnóstico (HU #13846, decisión Q1 del 2026-10-22): los PDF, Excel y ZIP que se entregan al cliente.
/// Van al file-manager corporativo (MinIO en DEV/QA, Contabo en PDN) en la categoría <c>diagnostico</c>, hermana de
/// <c>tramites</c>, <c>ict</c> y <c>comparendos</c>. Guardar en la base (<c>diagnostico.archivo_local</c>) solo se permite con
/// <see cref="OpcionesAlmacen.GuardarEnBase"/> (pruebas automáticas): sin URL y sin esa bandera el servicio no arranca, para que
/// un <c>.env</c> incompleto no deje los archivos del cliente en la base sin que nadie lo note.
/// </summary>
public interface IAlmacenArchivos
{
    Task<ArchivoGuardado> GuardarAsync(Guid empresaId, string tipo, string nombre, string contentType, byte[] contenido, CancellationToken ct);

    /// <summary>Los bytes del archivo, o null si ya no existe.</summary>
    Task<byte[]?> LeerAsync(string referencia, CancellationToken ct);

    /// <summary>
    /// Guarda el archivo que está en disco en <paramref name="ruta"/> (el ZIP del lote, HU #13850, decisión Q5: se escribe en un
    /// temporal y no se tiene entero en memoria). El file-manager lo sube leyendo del disco; por defecto (la base, las pruebas) se
    /// leen sus bytes. Quien llama borra el temporal.
    /// </summary>
    async Task<ArchivoGuardado> GuardarArchivoAsync(Guid empresaId, string tipo, string nombre, string contentType, string ruta, CancellationToken ct) =>
        await GuardarAsync(empresaId, tipo, nombre, contentType, await File.ReadAllBytesAsync(ruta, ct).ConfigureAwait(false), ct).ConfigureAwait(false);

    /// <summary>
    /// El archivo como flujo para entregarlo sin cargarlo entero (el ZIP del lote, HU #13850), o null si ya no existe. El
    /// file-manager lo lee de su URL firmada; por defecto, los bytes de <see cref="LeerAsync"/>. Quien llama libera el flujo.
    /// </summary>
    async Task<Stream?> AbrirAsync(string referencia, CancellationToken ct) =>
        await LeerAsync(referencia, ct).ConfigureAwait(false) is { } bytes ? new MemoryStream(bytes, writable: false) : null;
}

/// <summary>
/// El almacén sobre un contexto de datos dado (HU #13850). En una petición, <see cref="IAlmacenArchivos"/> usa el contexto de la
/// petición; el trabajador del ZIP, que no tiene sesión, lo arma con esta fábrica sobre el contexto fijado a la empresa de su
/// trabajo (el respaldo en la base filtra y escribe con esa empresa). Las pruebas registran la suya antes que el servicio.
/// </summary>
public interface IFabricaAlmacen
{
    IAlmacenArchivos Para(DiagnosticoDb db);
}

internal sealed class FabricaAlmacenEnBase : IFabricaAlmacen
{
    public IAlmacenArchivos Para(DiagnosticoDb db) => new AlmacenEnBase(db);
}

internal sealed class FabricaAlmacenFileManager(FileManagerAlmacen fileManager) : IFabricaAlmacen
{
    public IAlmacenArchivos Para(DiagnosticoDb db) => new AlmacenFileManagerConRespaldo(fileManager, db);
}

public sealed class FileManagerOptions
{
    public string BaseUrl { get; set; } = string.Empty;
    public string FilesPath { get; set; } = "api/v1/files";
    public string Category { get; set; } = "diagnostico";
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
/// Cliente del file-manager (mismo contrato que <c>FileManagerAttachmentStorage</c> de core-api): POST /files crea el registro y
/// devuelve la URL firmada de subida (POST policy o PUT, lo decide el file-manager); GET /files/{id}/presigned-url da la de
/// descarga. No hay borrado: el file-manager lo limpia solo, y las evidencias reemplazadas conservan su archivo (R-18).
/// </summary>
internal sealed class FileManagerAlmacen(HttpClient http, IOptions<FileManagerOptions> options) : IAlmacenArchivos
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly FileManagerOptions _o = options.Value;

    public async Task<ArchivoGuardado> GuardarAsync(Guid empresaId, string tipo, string nombre, string contentType, byte[] contenido, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(contenido);
        var sha = Convert.ToHexStringLower(SHA256.HashData(contenido));
        var (id, firmada, url, archivo) = await CrearRegistroAsync(empresaId, tipo, nombre, sha, ct).ConfigureAwait(false);
        await SubirAsync(firmada, url, () => Crudo(contenido), archivo, ct).ConfigureAwait(false);
        return new ArchivoGuardado(id, sha, contenido.LongLength);
    }

    /// <summary>Como <see cref="GuardarAsync"/>, pero subiendo desde el disco (cada intento abre el archivo de nuevo).</summary>
    public async Task<ArchivoGuardado> GuardarArchivoAsync(Guid empresaId, string tipo, string nombre, string contentType, string ruta, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ruta);
        string sha;
        long tamano;
        await using (var lectura = File.OpenRead(ruta))
        {
            sha = Convert.ToHexStringLower(await SHA256.HashDataAsync(lectura, ct).ConfigureAwait(false));
            tamano = lectura.Length;
        }

        var (id, firmada, url, archivo) = await CrearRegistroAsync(empresaId, tipo, nombre, sha, ct).ConfigureAwait(false);
        await SubirAsync(firmada, url, () => Crudo(File.OpenRead(ruta)), archivo, ct).ConfigureAwait(false);
        return new ArchivoGuardado(id, sha, tamano);
    }

    /// <summary>El archivo leído de su URL firmada como flujo (sin cargarlo entero); liberar el flujo libera la respuesta.</summary>
    public async Task<Stream?> AbrirAsync(string referencia, CancellationToken ct)
    {
        if (await UrlDescargaAsync(referencia, ct).ConfigureAwait(false) is not { } url)
            return null;
        var resp = await http.GetAsync(new Uri(url), HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        try
        {
            if (resp.StatusCode == HttpStatusCode.NotFound)
            {
                resp.Dispose();
                return null;
            }

            resp.EnsureSuccessStatusCode();
            return await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        }
        catch
        {
            resp.Dispose();
            throw;
        }
    }

    private async Task<(string Id, UrlFirmada Firmada, string Url, string Archivo)> CrearRegistroAsync(
        Guid empresaId, string tipo, string nombre, string sha, CancellationToken ct)
    {
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
        return (creado.Id, creado.PresignedUrl, url, archivo);
    }

    private async Task<string?> UrlDescargaAsync(string referencia, CancellationToken ct)
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
        return body?.PresignedUrl?.Url is { Length: > 0 } url ? url : null;
    }

    public async Task<byte[]?> LeerAsync(string referencia, CancellationToken ct) =>
        await UrlDescargaAsync(referencia, ct).ConfigureAwait(false) is { } url
            ? await http.GetByteArrayAsync(new Uri(url), ct).ConfigureAwait(false)
            : null;

    private async Task SubirAsync(UrlFirmada firmada, string url, Func<HttpContent> crudo, string nombre, CancellationToken ct)
    {
        // PUT existe porque hay gateways (Contabo) que rechazan el POST policy. Un reintento ante fallos de red.
        var put = string.Equals(firmada.Method, "PUT", StringComparison.OrdinalIgnoreCase);
        Exception? ultimo = null;
        for (var intento = 0; intento < 2; intento++)
        {
            try
            {
                using HttpContent contenido = put ? crudo() : Formulario(firmada, crudo(), nombre);
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

    private static StreamContent Crudo(Stream flujo)
    {
        var c = new StreamContent(flujo);
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
    /// <summary>Bandera explícita para guardar los archivos en la base. Solo la ponen las pruebas automáticas.</summary>
    public const string GuardarEnBase = "Diagnostico:Archivos:GuardarEnBase";
}

/// <summary>
/// File-manager para lo nuevo; lo que ya tenga referencia <c>db:</c> (guardado en la base antes de conectar el file-manager,
/// solo en bases locales) se sigue leyendo de la base.
/// </summary>
internal sealed class AlmacenFileManagerConRespaldo(FileManagerAlmacen fileManager, DiagnosticoDb db) : IAlmacenArchivos
{
    public Task<ArchivoGuardado> GuardarAsync(Guid empresaId, string tipo, string nombre, string contentType, byte[] contenido, CancellationToken ct) =>
        fileManager.GuardarAsync(empresaId, tipo, nombre, contentType, contenido, ct);

    public Task<byte[]?> LeerAsync(string referencia, CancellationToken ct) =>
        referencia.StartsWith(AlmacenEnBase.Prefijo, StringComparison.Ordinal)
            ? AlmacenEnBase.LeerDeBaseAsync(db, referencia, ct)
            : fileManager.LeerAsync(referencia, ct);

    public Task<ArchivoGuardado> GuardarArchivoAsync(Guid empresaId, string tipo, string nombre, string contentType, string ruta, CancellationToken ct) =>
        fileManager.GuardarArchivoAsync(empresaId, tipo, nombre, contentType, ruta, ct);

    public async Task<Stream?> AbrirAsync(string referencia, CancellationToken ct) =>
        referencia.StartsWith(AlmacenEnBase.Prefijo, StringComparison.Ordinal)
            ? await AlmacenEnBase.LeerDeBaseAsync(db, referencia, ct).ConfigureAwait(false) is { } bytes ? new MemoryStream(bytes, writable: false) : null
            : await fileManager.AbrirAsync(referencia, ct).ConfigureAwait(false);
}

/// <summary>
/// Un archivo guardado en la base (<c>diagnostico.archivo_local</c>) cuando no hay file-manager: solo pruebas automáticas
/// (<see cref="OpcionesAlmacen.GuardarEnBase"/>). Es de la empresa del token, como toda entidad de negocio.
/// </summary>
public sealed class ArchivoLocal : IEntidadDeEmpresa
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public Guid TenantId { get; set; }

    /// <summary>Tipo de archivo (PDF, XLSX, ZIP).</summary>
    public required string Tipo { get; init; }

    public required string Nombre { get; init; }

    public required string ContentType { get; init; }

    public required byte[] Contenido { get; init; }

    public DateTimeOffset CreadoEn { get; init; }
}

/// <summary>Sin file-manager (solo pruebas automáticas): los bytes quedan en <c>diagnostico.archivo_local</c>, de la empresa del token.</summary>
internal sealed class AlmacenEnBase(DiagnosticoDb db) : IAlmacenArchivos
{
    public const string Prefijo = "db:";

    public async Task<ArchivoGuardado> GuardarAsync(Guid empresaId, string tipo, string nombre, string contentType, byte[] contenido, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(contenido);
        var archivo = new ArchivoLocal
        {
            TenantId = empresaId,
            Tipo = tipo,
            Nombre = nombre,
            ContentType = contentType,
            Contenido = contenido,
            CreadoEn = DateTimeOffset.UtcNow,
        };
        db.Add(archivo);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        db.Entry(archivo).State = EntityState.Detached;
        return new ArchivoGuardado($"{Prefijo}{archivo.Id:D}", Convert.ToHexStringLower(SHA256.HashData(contenido)), contenido.LongLength);
    }

    public Task<byte[]?> LeerAsync(string referencia, CancellationToken ct) => LeerDeBaseAsync(db, referencia, ct);

    public static async Task<byte[]?> LeerDeBaseAsync(DiagnosticoDb db, string referencia, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        return referencia is not null && referencia.StartsWith(Prefijo, StringComparison.Ordinal) && Guid.TryParse(referencia[Prefijo.Length..], out var id)
            ? await db.Set<ArchivoLocal>().AsNoTracking().Where(a => a.Id == id).Select(a => a.Contenido).FirstOrDefaultAsync(ct).ConfigureAwait(false)
            : null;
    }
}

public static class AlmacenArchivosExtensions
{
    /// <summary>
    /// File-manager con <c>FILE_MANAGER_BASE_URL</c> (o <c>FileManager:BaseUrl</c>); sin URL, la base solo con
    /// <see cref="OpcionesAlmacen.GuardarEnBase"/>=true; sin ninguna de las dos, el servicio no arranca. Con la misma elección se
    /// registra <see cref="IFabricaAlmacen"/> (TryAdd: las pruebas ponen la suya antes), con la que el trabajador del ZIP arma el
    /// almacén sobre el contexto fijado a la empresa de su trabajo.
    /// </summary>
    public static IServiceCollection AddAlmacenArchivos(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var opciones = new FileManagerOptions();
        FileManagerOptions.Configurar(opciones, configuration);
        services.Configure<FileManagerOptions>(o => FileManagerOptions.Configurar(o, configuration));
        if (string.IsNullOrWhiteSpace(opciones.BaseUrl))
        {
            if (!configuration.GetValue(OpcionesAlmacen.GuardarEnBase, false))
                throw new InvalidOperationException(
                    "Diagnóstico no tiene file-manager: define FILE_MANAGER_BASE_URL (la misma URL que core-api; MinIO en DEV/QA, "
                    + "Contabo en PDN, que además exige FILE_MANAGER_AUTH_TOKEN). Guardar los archivos en la base solo se permite "
                    + $"con {OpcionesAlmacen.GuardarEnBase}=true, que es para las pruebas automáticas.");
            services.AddScoped<IAlmacenArchivos, AlmacenEnBase>();
            services.TryAddSingleton<IFabricaAlmacen, FabricaAlmacenEnBase>();
            return services;
        }

        services.AddHttpClient<FileManagerAlmacen>(c =>
        {
            c.BaseAddress = new Uri(opciones.BaseUrl.EndsWith('/') ? opciones.BaseUrl : opciones.BaseUrl + "/");
            c.Timeout = TimeSpan.FromSeconds(opciones.TimeoutSeconds);
        });
        services.AddScoped<IAlmacenArchivos, AlmacenFileManagerConRespaldo>();
        services.TryAddScoped<IFabricaAlmacen, FabricaAlmacenFileManager>();
        return services;
    }
}
