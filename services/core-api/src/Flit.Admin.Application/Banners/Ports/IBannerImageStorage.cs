namespace Flit.Admin.Application.Banners.Ports;

/// <summary>Archivo ya persistido en el storage: path opaco + hash + tamano.</summary>
public sealed record StoredBannerImage(string StoragePath, string Sha256, long SizeBytes);

/// <summary>
/// Puerto ACOTADO de almacenamiento del modulo de banners (Feature #12236, HU #12239/#12240,
/// ADR-0057-banners-imagen-endpoint-propio-sin-presigned). El adaptador de Infrastructure delega
/// en <c>IAttachmentStorage</c> pasando una clave de agrupacion FIJA (no un tenantId: los banners
/// son globales, ADR-0058) - mismo patron que
/// <c>GeneracionDocumental.Ports.IStandaloneDocumentStorage</c> (ADR-0056). No se modifica
/// <c>IAttachmentStorage</c>.
/// <para>Existe ademas por la restriccion de compilacion C6: Flit.Admin.Application no referencia
/// Flit.Tramites.Application y no puede nombrar sus tipos.</para>
/// <para><see cref="SaveAsync"/> lo usa el CRUD administrable (HU #12239); <see cref="OpenReadAsync"/>
/// lo usa el endpoint de streaming publico y anonimo <c>GET /public/banners/{id}/image</c>
/// (HU #12240) — ambas HUs se implementaron en paralelo y declararon este puerto de forma
/// independiente, por lo que su union en un solo archivo es el resultado esperado del merge.</para>
/// </summary>
public interface IBannerImageStorage
{
    /// <summary>
    /// Persiste el binario de la imagen del banner y devuelve su identificador de almacenamiento,
    /// el SHA-256 (usado como ETag por el endpoint de lectura de HU #12240) y el tamano.
    /// </summary>
    Task<StoredBannerImage> SaveAsync(
        string filename,
        Stream content,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Abre el binario de la imagen para lectura/streaming. Devuelve <c>null</c> si el archivo
    /// ya no existe en el backend de almacenamiento (el banner queda en BD pero el binario se
    /// perdio).
    /// </summary>
    Task<Stream?> OpenReadAsync(string storagePath, CancellationToken cancellationToken = default);
}
