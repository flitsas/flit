using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Flit.PersonaNatural.Api.Solicitudes;
using Microsoft.Extensions.Options;

namespace Flit.PersonaNatural.Api.Adjuntos;

/// <summary>Lo que devuelve el almacenamiento al guardar: la referencia opaca (lo único que se guarda en <c>adjunto</c>), el hash y el tamaño.</summary>
public sealed record ArchivoGuardado(string Referencia, string Sha256, long TamanoBytes);

/// <summary>
/// Dónde viven los archivos del ciudadano (HU #14048). El portal NO los guarda en su base ni en disco: solo la referencia
/// opaca. La implementación de producción es el file-manager corporativo (<see cref="AlmacenFileManager"/>, el mismo que usa
/// core-api para los adjuntos de trámites).
/// </summary>
public interface IAlmacenAdjuntos
{
    Task<ArchivoGuardado> GuardarAsync(Guid solicitudId, string tipo, string nombre, byte[] contenido, CancellationToken ct);

    /// <summary>Retira el archivo. Con el file-manager actual es un no-op documentado (no expone borrado); ver <see cref="AlmacenFileManager"/>.</summary>
    Task EliminarAsync(string referencia, CancellationToken ct);
}

/// <summary>
/// Costura para un antivirus o análisis de contenido (HU #14048): hoy no hay uno contratado, así que el analizador por
/// defecto lo acepta todo. Cuando exista, se registra otra implementación y el archivo se rechaza antes de guardarse.
/// </summary>
public interface IAnalizadorDeArchivos
{
    /// <summary>Devuelve el motivo por el que se rechaza el archivo, o <c>null</c> si está limpio.</summary>
    Task<string?> AnalizarAsync(byte[] contenido, string mime, CancellationToken ct);
}

internal sealed class AnalizadorDeArchivosNulo : IAnalizadorDeArchivos
{
    public Task<string?> AnalizarAsync(byte[] contenido, string mime, CancellationToken ct) => Task.FromResult<string?>(null);
}

/// <summary>Límites de carga (sección <c>Adjuntos</c>).</summary>
public sealed class AdjuntosOptions
{
    public const string SectionName = "Adjuntos";

    /// <summary>Tope por archivo: 20 MB.</summary>
    public long MaxBytes { get; set; } = 20L * 1024 * 1024;

    /// <summary>Tope de archivos por solicitud, para que una sesión no llene el almacenamiento.</summary>
    public int MaxPorSolicitud { get; set; } = 30;
}

/// <summary>Opciones del file-manager. Mismos nombres que en core-api: <c>FileManager:*</c> o las variables <c>FILE_MANAGER_*</c>.</summary>
public sealed class FileManagerOptions
{
    public const string SectionName = "FileManager";

    public string BaseUrl { get; set; } = string.Empty;

    public string FilesPath { get; set; } = "api/v1/files";

    /// <summary>Carpeta lógica de los archivos del portal, aparte de la de los trámites de FLIT 2.</summary>
    public string Category { get; set; } = "persona-natural";

    public int TimeoutSeconds { get; set; } = 30;

    public string AuthToken { get; set; } = string.Empty;

    public static void Configurar(FileManagerOptions o, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(o);
        ArgumentNullException.ThrowIfNull(configuration);
        string? Cfg(string clave, string env) => configuration[clave] ?? Environment.GetEnvironmentVariable(env);
        o.BaseUrl = Cfg("FileManager:BaseUrl", "FILE_MANAGER_BASE_URL") ?? o.BaseUrl;
        o.FilesPath = Cfg("FileManager:FilesPath", "FILE_MANAGER_FILES_PATH") ?? o.FilesPath;
        o.Category = Cfg("FileManager:Category", "FILE_MANAGER_CATEGORY_PN") ?? o.Category;
        o.TimeoutSeconds = int.TryParse(Cfg("FileManager:TimeoutSeconds", "FILE_MANAGER_TIMEOUT_SECONDS"), out var t) ? t : o.TimeoutSeconds;
        o.AuthToken = Cfg("FileManager:AuthToken", "FILE_MANAGER_AUTH_TOKEN") ?? o.AuthToken;
    }
}

/// <summary>Sin file-manager configurado el servicio arranca igual y cargar un archivo responde 503 con código estable.</summary>
internal sealed class AlmacenAdjuntosNoConfigurado : IAlmacenAdjuntos
{
    private static ReglaDeNegocioException NoDisponible() => new(
        StatusCodes.Status503ServiceUnavailable, "almacenamiento_no_disponible", "Almacenamiento no disponible", "No podemos recibir archivos por ahora. Intente más tarde.");

    public Task<ArchivoGuardado> GuardarAsync(Guid solicitudId, string tipo, string nombre, byte[] contenido, CancellationToken ct) => throw NoDisponible();

    public Task EliminarAsync(string referencia, CancellationToken ct) => throw NoDisponible();
}

/// <summary>
/// Almacenamiento sobre el file-manager corporativo (S3 con URLs prefirmadas), igual que el de adjuntos de core-api: crea el
/// registro (<c>POST api/v1/files</c>), sube los bytes a la URL prefirmada y guarda solo el <c>id</c> que devuelve.
/// <para>
/// Borrado: el file-manager no expone borrado; el objeto queda huérfano y lo recupera su job de limpieza (cold storage a los 30
/// días). Es una limitación conocida que Seguridad (HU I2) debe revisar frente a la retención de Habeas Data. Sin antivirus
/// real: ver <see cref="IAnalizadorDeArchivos"/>.
/// </para>
/// </summary>
internal sealed class AlmacenFileManager(HttpClient http, IOptions<FileManagerOptions> options) : IAlmacenAdjuntos
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly FileManagerOptions _o = options.Value;

    public async Task<ArchivoGuardado> GuardarAsync(Guid solicitudId, string tipo, string nombre, byte[] contenido, CancellationToken ct)
    {
        var sha256 = Convert.ToHexStringLower(SHA256.HashData(contenido));
        var crear = new CrearArchivo(_o.Category, nombre, [tipo], new Dictionary<string, string> { ["solicitudId"] = solicitudId.ToString("D"), ["tipo"] = tipo, ["sha256"] = sha256 });
        using var peticion = new HttpRequestMessage(HttpMethod.Post, _o.FilesPath) { Content = new StringContent(JsonSerializer.Serialize(crear, Json), Encoding.UTF8, "application/json") };
        if (!string.IsNullOrWhiteSpace(_o.AuthToken))
            peticion.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _o.AuthToken);
        using var respuesta = await http.SendAsync(peticion, ct).ConfigureAwait(false);
        respuesta.EnsureSuccessStatusCode();
        var creado = await respuesta.Content.ReadFromJsonAsync<ArchivoCreado>(Json, ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(creado?.Id) || string.IsNullOrWhiteSpace(creado.PresignedUrl?.Url))
            throw new InvalidOperationException("file-manager: respuesta de creación inválida.");

        await SubirAsync(creado.PresignedUrl, contenido, nombre, ct).ConfigureAwait(false);
        return new ArchivoGuardado(creado.Id, sha256, contenido.LongLength);
    }

    public Task EliminarAsync(string referencia, CancellationToken ct) => Task.CompletedTask;

    /// <summary>El método lo decide el file-manager (POST con campos firmados o PUT con los bytes crudos); sin dato, POST. Un reintento ante fallos de red.</summary>
    private async Task SubirAsync(UrlPrefirmada url, byte[] contenido, string nombre, CancellationToken ct)
    {
        var put = string.Equals(url.Method, "PUT", StringComparison.OrdinalIgnoreCase);
        Exception? ultimo = null;
        for (var intento = 0; intento < 2; intento++)
        {
            try
            {
                using HttpContent cuerpo = put ? Crudo(contenido) : Formulario(url, contenido, nombre);
                using var respuesta = put ? await http.PutAsync(url.Url, cuerpo, ct).ConfigureAwait(false) : await http.PostAsync(url.Url, cuerpo, ct).ConfigureAwait(false);
                respuesta.EnsureSuccessStatusCode();
                return;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                ultimo = ex;
            }
        }

        throw new InvalidOperationException("file-manager: no se pudo subir el archivo.", ultimo);
    }

    private static ByteArrayContent Crudo(byte[] contenido)
    {
        var c = new ByteArrayContent(contenido);
        c.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        return c;
    }

    private static MultipartFormDataContent Formulario(UrlPrefirmada url, byte[] contenido, string nombre)
    {
        var form = new MultipartFormDataContent();
        if (url.Fields is not null)
            foreach (var (clave, valor) in url.Fields)
                form.Add(new StringContent(valor), clave);
        var archivo = new ByteArrayContent(contenido);
        archivo.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(archivo, "file", nombre);
        return form;
    }

    private sealed record CrearArchivo(
        [property: JsonPropertyName("category")] string Category,
        [property: JsonPropertyName("filename")] string Filename,
        [property: JsonPropertyName("tags")] IReadOnlyList<string> Tags,
        [property: JsonPropertyName("metadata")] IReadOnlyDictionary<string, string> Metadata);

    private sealed record ArchivoCreado(
        [property: JsonPropertyName("id")] string? Id,
        [property: JsonPropertyName("presignedUrl")] UrlPrefirmada? PresignedUrl);

    private sealed record UrlPrefirmada(
        [property: JsonPropertyName("url")] string? Url,
        [property: JsonPropertyName("method")] string? Method,
        [property: JsonPropertyName("fields")] Dictionary<string, string>? Fields);
}
