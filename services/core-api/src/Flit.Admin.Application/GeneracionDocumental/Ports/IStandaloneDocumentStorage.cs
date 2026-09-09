namespace Flit.Admin.Application.GeneracionDocumental.Ports;

/// <summary>Archivo ya persistido en el storage: path opaco + hash + tamaño.</summary>
public sealed record StoredStandaloneDocument(string StoragePath, string Sha256, long SizeBytes);

/// <summary>
/// Enlace de descarga de vida corta (presigned GET, ADR-0029). <b>La URL lleva firma HMAC: no se
/// escribe en ningún log, traza de error ni comentario de auditoría</b>; solo viaja en el cuerpo de
/// la respuesta al usuario que ya demostró permiso y pertenencia al tenant.
/// </summary>
public sealed record StandaloneDocumentDownloadLink(string Url, DateTimeOffset ExpiresAt);

/// <summary>
/// Puerto ACOTADO de almacenamiento del módulo de generación documental (Feature #12201,
/// ADR-0056-generacion-documental-standalone). El adaptador de Infrastructure delega en
/// <c>IAttachmentStorage</c> pasando el <b>tenantId</b> como clave de agrupación — que es lo que ese
/// primer parámetro realmente significa, con cuatro precedentes productivos (baúl de firmas,
/// improntas de identidad, escrituras y plantillas de mandato). <b>No se modifica
/// <c>IAttachmentStorage</c>.</b>
/// <para>Existe además por una restricción de compilación (C6): <c>Flit.Admin.Application</c> no
/// referencia <c>Flit.Tramites.Application</c> y no puede nombrar sus tipos.</para>
/// </summary>
public interface IStandaloneDocumentStorage
{
    /// <summary>
    /// Persiste el binario del documento generado y devuelve su identificador de almacenamiento,
    /// el SHA-256 y el tamaño.
    /// </summary>
    /// <param name="tenantId">Clave de agrupación del artefacto en el storage (no es una FK).</param>
    Task<StoredStandaloneDocument> SaveAsync(
        Guid tenantId,
        string tipo,
        string filename,
        Stream content,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Abre el binario para lectura. Devuelve <c>null</c> si el archivo ya no existe.
    /// <para>Lo usa el worker de lotes (Feature #12201, I3) para RELEER el XLSX fuente: las filas
    /// del archivo no se copian a ninguna columna —no hay dónde, y serían PII duplicada—, así que
    /// la fuente de verdad de lo cargado es el propio archivo en storage.</para>
    /// </summary>
    Task<Stream?> OpenReadAsync(string storagePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Presigned GET de vida corta para el PDF ya generado (CF-19, ADR-0029). Devuelve <c>null</c>
    /// si el binario no existe o el backend de almacenamiento no soporta presigned view.
    /// <para>El caller NO puede loguear la URL devuelta.</para>
    /// </summary>
    Task<StandaloneDocumentDownloadLink?> GetPresignedViewUrlAsync(
        string storagePath,
        CancellationToken cancellationToken = default);
}
