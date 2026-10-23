using Flit.Comparendos.Api.Infraestructura.Archivos;
using Flit.Comparendos.Api.Persistence;
using Flit.Comparendos.Api.Persistence.Modelo;
using Flit.Comparendos.Api.Plataforma;
using Microsoft.EntityFrameworkCore;

namespace Flit.Comparendos.Api.Modulos.Comun;

/// <summary>
/// Archivos del producto (§3.13): guarda los bytes en el almacén y deja la fila <c>archivo</c> en el contexto (la
/// confirma el SaveChanges de quien llama). Valida tipo y tamaño de lo que suben los usuarios.
/// </summary>
public sealed class RegistroArchivos(ComparendosDb db, IAlmacenArchivos almacen, Reloj reloj)
{
    public const long MaximoDocumento = 10L * 1024 * 1024;

    public static readonly string[] Documentos = ["application/pdf", "image/jpeg", "image/png"];
    public static readonly string[] Imagenes = ["image/png", "image/jpeg"];

    public async Task<Guid> GuardarAsync(Guid? tenantId, string dominio, string nombre, string contentType, byte[] contenido, CancellationToken ct, Guid? usuario = null)
    {
        ArgumentNullException.ThrowIfNull(contenido);
        var guardado = await almacen.GuardarAsync(tenantId ?? Guid.Empty, dominio, nombre, contentType, contenido, ct).ConfigureAwait(false);
        var archivo = new Archivo
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            Referencia = guardado.Referencia,
            Dominio = dominio,
            NombreOriginal = nombre.Length > 255 ? nombre[^255..] : nombre,
            ContentType = contentType,
            TamanoBytes = guardado.Tamano,
            Sha256 = guardado.Sha256,
            CreadoEn = reloj.Ahora,
            CreadoPor = usuario,
        };
        db.Archivos.Add(archivo);
        return archivo.Id;
    }

    /// <summary>Lo que sube un usuario: valida tipo (por content type y extensión) y tamaño antes de guardar.</summary>
    public async Task<Guid> SubirAsync(Guid tenantId, string dominio, IFormFile archivo, string[] permitidos, CancellationToken ct, Guid? usuario = null, long maximo = MaximoDocumento)
    {
        ArgumentNullException.ThrowIfNull(archivo);
        ArgumentNullException.ThrowIfNull(permitidos);
        var tipo = TipoDe(archivo);
        if (!permitidos.Contains(tipo))
            throw new ReglaException("ARCHIVO_TIPO_INVALIDO", $"Tipo de archivo no permitido ({Path.GetExtension(archivo.FileName)}). Se aceptan: {string.Join(", ", permitidos.Select(Extension))}.");
        if (archivo.Length is 0 || archivo.Length > maximo)
            throw new ReglaException("ARCHIVO_TAMANO_INVALIDO", $"El archivo debe pesar entre 1 byte y {maximo / 1024 / 1024} MB.");

        await using var stream = archivo.OpenReadStream();
        using var memoria = new MemoryStream();
        await stream.CopyToAsync(memoria, ct).ConfigureAwait(false);
        return await GuardarAsync(tenantId, dominio, Path.GetFileName(archivo.FileName), tipo, memoria.ToArray(), ct, usuario).ConfigureAwait(false);
    }

    public async Task<(Archivo Archivo, byte[] Contenido)?> LeerAsync(Guid archivoId, CancellationToken ct)
    {
        var archivo = await db.Archivos.AsNoTracking().FirstOrDefaultAsync(a => a.Id == archivoId, ct).ConfigureAwait(false);
        if (archivo is null)
            return null;
        var bytes = await almacen.LeerAsync(archivo.Referencia, ct).ConfigureAwait(false);
        return bytes is null ? null : (archivo, bytes);
    }

    public static string TipoDe(IFormFile archivo)
    {
        ArgumentNullException.ThrowIfNull(archivo);
        return Path.GetExtension(archivo.FileName).ToUpperInvariant() switch
        {
            ".PDF" => "application/pdf",
            ".JPG" or ".JPEG" => "image/jpeg",
            ".PNG" => "image/png",
            ".TIF" or ".TIFF" => "image/tiff",
            ".XLSX" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            _ => archivo.ContentType ?? "application/octet-stream",
        };
    }

    private static string Extension(string tipo) => tipo switch
    {
        "application/pdf" => "PDF",
        "image/jpeg" => "JPG",
        "image/png" => "PNG",
        "image/tiff" => "TIFF",
        _ => tipo,
    };
}

/// <summary>Descarga de un archivo: el de la empresa del usuario; los globales (alertas internas, respuestas crudas de las fuentes) y los de cualquier empresa, solo el SuperAdmin.</summary>
public static class ArchivosEndpoints
{
    public static void Map(RouteGroupBuilder api) =>
        api.MapGet("/archivos/{id:guid}", Descargar).RequireAuthorization(AutorizacionExtensions.PoliticaUsuario);

    private static async Task<IResult> Descargar(Guid id, bool? inline, RegistroArchivos archivos, UsuarioActual usuario, CancellationToken ct)
    {
        var leido = await archivos.LeerAsync(id, ct).ConfigureAwait(false) ?? throw ReglaException.NoEncontrado("El archivo");
        if (!usuario.EsSuperAdmin && (leido.Archivo.TenantId is not { } dueno || dueno == Guid.Empty || dueno != usuario.EmpresaId))
            throw ReglaException.NoEncontrado("El archivo");
        return inline == true
            ? Results.File(leido.Contenido, leido.Archivo.ContentType)
            : Results.File(leido.Contenido, leido.Archivo.ContentType, leido.Archivo.NombreOriginal);
    }
}
