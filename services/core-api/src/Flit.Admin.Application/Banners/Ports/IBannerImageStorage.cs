namespace Flit.Admin.Application.Banners.Ports;

/// <summary>Archivo ya persistido en el storage: path opaco + hash + tamano.</summary>
public sealed record StoredBannerImage(string StoragePath, string Sha256, long SizeBytes);

/// <summary>
/// Puerto ACOTADO de almacenamiento del modulo de banners (HU #12239, ADR-0057). El adaptador de
/// Infrastructure delega en IAttachmentStorage pasando una clave de agrupacion FIJA (no un
/// tenantId: los banners son globales, ADR-0058) - mismo patron que
/// GeneracionDocumental.Ports.IStandaloneDocumentStorage. No se modifica IAttachmentStorage.
/// <para>Existe ademas por la restriccion de compilacion C6: Flit.Admin.Application no referencia
/// Flit.Tramites.Application y no puede nombrar sus tipos.</para>
/// <para>Solo cubre escritura/lectura interna (CRUD, HU #12239). El endpoint de streaming publico
/// GET /public/banners/{id}/image (lectura anonima, ADR-0057) es alcance de HU3 y no consume este
/// puerto directamente desde Admin.Application.</para>
/// </summary>
public interface IBannerImageStorage
{
    /// <summary>
    /// Persiste el binario de la imagen del banner y devuelve su identificador de almacenamiento,
    /// el SHA-256 (usado como ETag por el endpoint de lectura de HU3) y el tamano.
    /// </summary>
    Task<StoredBannerImage> SaveAsync(
        string filename,
        Stream content,
        CancellationToken cancellationToken = default);
}
