using Flit.PersonaNatural.Api.Persistence;
using Flit.PersonaNatural.Api.Sesiones;
using Flit.PersonaNatural.Api.Solicitudes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Flit.PersonaNatural.Api.Adjuntos;

/// <summary>
/// Archivos del trámite y comprobante de pago (HU #14048, ADR-0075). Se valida lo que el archivo ES (firma de los primeros
/// bytes), su tamaño y que el documento pertenezca al tipo (la lista sale del snapshot de la solicitud, no de código por
/// tipo); en la base solo queda la referencia opaca al almacenamiento. Ningún log lleva el nombre ni el contenido.
/// </summary>
public sealed class AdjuntosService(
    PersonaNaturalDb db,
    SolicitudesService solicitudes,
    IAlmacenAdjuntos almacen,
    IAnalizadorDeArchivos analizador,
    IOptions<AdjuntosOptions> options,
    TimeProvider reloj)
{
    public const string CodigoNoPermitido = "adjunto_no_permitido";
    public const string CodigoDocumentoAjeno = "documento_no_pertenece_al_tipo";
    public const string CodigoAdjuntoAjeno = "adjunto_ajeno";
    public const string AccionEliminado = "adjunto_eliminado";

    private readonly AdjuntosOptions _o = options.Value;

    /// <summary>
    /// Guarda un archivo ligado a un documento del tipo (<paramref name="tipo"/> es su <c>doc_code</c>) o, con
    /// <see cref="SolicitudesService.TipoComprobantePago"/>, como soporte del pago: el nuevo comprobante reemplaza al anterior
    /// para efectos del requisito, y el anterior queda en el historial de la solicitud (supuesto S1: lo valida un operador FLIT).
    /// </summary>
    public async Task<AdjuntoDto> CargarAsync(Guid solicitudId, string? tipo, string? nombreOriginal, byte[] contenido, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(contenido);
        var (s, def) = await solicitudes.CargarParaEditarAsync(solicitudId, null, ct).ConfigureAwait(false);

        tipo = tipo?.Trim();
        if (string.IsNullOrEmpty(tipo))
            throw Invalido("tipo", "Indique a qué documento corresponde el archivo.");
        if (tipo != SolicitudesService.TipoComprobantePago && !def.Documentos.Any(d => string.Equals(d.DocCode, tipo, StringComparison.Ordinal)))
            throw new ReglaDeNegocioException(StatusCodes.Status422UnprocessableEntity, CodigoDocumentoAjeno, "Documento no válido", "Ese documento no hace parte de este tipo de trámite.");

        if (contenido.Length == 0)
            throw Invalido("archivo", "El archivo está vacío.");
        if (contenido.LongLength > _o.MaxBytes)
            throw NoPermitido(StatusCodes.Status413PayloadTooLarge, $"El archivo supera los {_o.MaxBytes / (1024 * 1024)} MB permitidos.");

        var mime = FirmaDeArchivo.Detectar(contenido);
        if (mime is null)
            throw NoPermitido(StatusCodes.Status415UnsupportedMediaType, "Solo se aceptan archivos PDF, JPG, PNG o WEBP.");
        if (FirmaDeArchivo.SegunExtension(nombreOriginal) != mime)
            throw NoPermitido(StatusCodes.Status415UnsupportedMediaType, "El contenido del archivo no coincide con su extensión.");

        if (await db.Set<Adjunto>().CountAsync(a => a.SolicitudId == solicitudId, ct).ConfigureAwait(false) >= _o.MaxPorSolicitud)
            throw new ReglaDeNegocioException(StatusCodes.Status422UnprocessableEntity, "limite_de_adjuntos", "Demasiados archivos", $"Un trámite admite hasta {_o.MaxPorSolicitud} archivos.");

        if (await analizador.AnalizarAsync(contenido, mime, ct).ConfigureAwait(false) is { } rechazo)
            throw NoPermitido(StatusCodes.Status415UnsupportedMediaType, rechazo);

        var nombre = FirmaDeArchivo.NombreSeguro(nombreOriginal, mime);
        ArchivoGuardado guardado;
        try
        {
            guardado = await almacen.GuardarAsync(solicitudId, tipo, nombre, contenido, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            throw new ReglaDeNegocioException(StatusCodes.Status502BadGateway, "almacenamiento_no_disponible", "Almacenamiento no disponible", "No pudimos guardar el archivo. Intente de nuevo.");
        }

        var fila = new Adjunto
        {
            Id = Guid.CreateVersion7(),
            SolicitudId = solicitudId,
            Tipo = tipo,
            NombreArchivo = nombre,
            MimeType = mime,
            TamanoBytes = guardado.TamanoBytes,
            Sha256 = guardado.Sha256,
            StoragePath = guardado.Referencia,
            CreatedAt = reloj.GetUtcNow(),
        };
        db.Add(fila);
        try
        {
            await solicitudes.GuardarAsync(s, ct).ConfigureAwait(false);
        }
        catch
        {
            // Si la base no aceptó el registro, el archivo no puede quedar huérfano en el almacenamiento.
            await almacen.EliminarAsync(guardado.Referencia, CancellationToken.None).ConfigureAwait(false);
            throw;
        }

        return new AdjuntoDto(fila.Id, fila.Tipo, fila.NombreArchivo, fila.MimeType, fila.TamanoBytes, fila.Sha256, fila.CreatedAt);
    }

    /// <summary>
    /// Elimina un archivo mientras la solicitud no esté en cola: sale del almacenamiento y de la solicitud, y queda la traza.
    /// Con un archivo de OTRA solicitud responde 403 y deja el intento en la auditoría.
    /// </summary>
    public async Task EliminarAsync(Guid solicitudId, Guid adjuntoId, CancellationToken ct)
    {
        var (s, _) = await solicitudes.CargarParaEditarAsync(solicitudId, null, ct).ConfigureAwait(false);
        var adjunto = await db.Set<Adjunto>().FirstOrDefaultAsync(a => a.Id == adjuntoId, ct).ConfigureAwait(false)
            ?? throw new ReglaDeNegocioException(StatusCodes.Status404NotFound, "adjunto_no_encontrado", "No encontrado", "No encontramos ese archivo.");

        var ahora = reloj.GetUtcNow();
        if (adjunto.SolicitudId != solicitudId)
        {
            db.Add(new AccesoPiiLog { Actor = "sesion", Accion = SesionEndpoints.AccionFueraDeAlcance, SolicitudId = solicitudId, OcurrioEn = ahora });
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            throw new ReglaDeNegocioException(StatusCodes.Status403Forbidden, CodigoAdjuntoAjeno, "Sin acceso", "Esta sesión no abre ese archivo.");
        }

        await almacen.EliminarAsync(adjunto.StoragePath, ct).ConfigureAwait(false);
        db.Remove(adjunto);
        db.Add(new AccesoPiiLog { Actor = "sesion", Accion = AccionEliminado, SolicitudId = solicitudId, OcurrioEn = ahora });
        await solicitudes.GuardarAsync(s, ct).ConfigureAwait(false);
    }

    private static ReglaDeNegocioException Invalido(string campo, string mensaje)
    {
        var errores = new ErroresDeCampo();
        errores.Agregar(campo, mensaje);
        return ReglaDeNegocioException.Invalido(errores);
    }

    private static ReglaDeNegocioException NoPermitido(int estado, string motivo) =>
        new(estado, CodigoNoPermitido, "Archivo no permitido", motivo, new Dictionary<string, object?> { ["motivo"] = motivo });
}

/// <summary>Cuando se cancela una solicitud (o su borrador se anonimiza) sus archivos salen del almacenamiento y de la base.</summary>
internal sealed class LimpiadorDeAdjuntos(PersonaNaturalDb db, IAlmacenAdjuntos almacen) : ILimpiadorDeSolicitud
{
    public async Task LimpiarAsync(Guid solicitudId, CancellationToken ct)
    {
        var referencias = await db.Set<Adjunto>().AsNoTracking().Where(a => a.SolicitudId == solicitudId).Select(a => a.StoragePath).ToListAsync(ct).ConfigureAwait(false);
        foreach (var referencia in referencias)
            await almacen.EliminarAsync(referencia, ct).ConfigureAwait(false);
        await db.Set<Adjunto>().Where(a => a.SolicitudId == solicitudId).ExecuteDeleteAsync(ct).ConfigureAwait(false);
    }
}
