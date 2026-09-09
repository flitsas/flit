namespace Flit.Admin.Application.Banners.Ports;

/// <summary>
/// Puerto ACOTADO de almacenamiento de imagenes de banners (Feature #12236,
/// ADR-0057-banners-imagen-endpoint-propio-sin-presigned). El adaptador de Infrastructure
/// delega en <c>IAttachmentStorage</c> (mismo patron que <c>IStandaloneDocumentStorage</c>,
/// ADR-0056). No se modifica <c>IAttachmentStorage</c>.
///
/// <para>HU #12240 (este archivo) solo necesita <see cref="OpenReadAsync"/> para el endpoint
/// de lectura publica de la imagen. HU #12239 (CRUD de banners, en desarrollo en paralelo)
/// anadira <c>SaveAsync</c> a esta misma interfaz — es un punto de conflicto de merge
/// esperado y aceptado (ver ADR-0057, notas para agentes).</para>
/// </summary>
public interface IBannerImageStorage
{
    /// <summary>
    /// Abre el binario de la imagen para lectura/streaming. Devuelve <c>null</c> si el archivo
    /// ya no existe en el backend de almacenamiento (el banner queda en BD pero el binario se
    /// perdio).
    /// </summary>
    Task<Stream?> OpenReadAsync(string storagePath, CancellationToken cancellationToken = default);
}
