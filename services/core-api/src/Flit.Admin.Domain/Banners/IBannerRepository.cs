using Flit.Admin.Domain.Common;

namespace Flit.Admin.Domain.Banners;

/// <summary>
/// Repositorio de banners promocionales (HU #12239) sobre <c>admin.banners</c> - tabla GLOBAL sin
/// tenant_id ni RLS (excepcion documentada en ADR-0058). Ninguna consulta filtra por tenant.
/// El soft-delete estandar (deleted_at IS NULL) si aplica.
/// </summary>
public interface IBannerRepository
{
    /// <summary>Crea un banner activo y devuelve el read model resultante (AC1).</summary>
    Task<BannerListItem> CreateAsync(
        string name,
        string imageStoragePath,
        string imageSha256,
        string? linkUrl,
        DateTimeOffset? validFrom,
        DateTimeOffset? validUntil,
        Guid? createdBy,
        CancellationToken cancellationToken = default);

    /// <summary>Listado paginado, mas reciente primero (AC2). Excluye soft-deleted salvo IncludeDeleted.</summary>
    Task<PagedResult<BannerListItem>> ListAsync(
        BannerListFilter filter,
        CancellationToken cancellationToken = default);

    /// <summary>Devuelve el banner por id (no eliminado), o null si no existe o esta eliminado (AC4).</summary>
    Task<BannerListItem?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Actualiza nombre, enlace y vigencia; si imageStoragePath no es null reemplaza tambien la
    /// imagen y su hash (recalculado por el caller via IBannerImageStorage.SaveAsync, invalidando
    /// el ETag que sirve HU3). Devuelve null si no existe o esta eliminado (AC4).
    /// </summary>
    Task<BannerListItem?> UpdateAsync(
        Guid id,
        string name,
        string? linkUrl,
        DateTimeOffset? validFrom,
        DateTimeOffset? validUntil,
        string? imageStoragePath,
        string? imageSha256,
        Guid? updatedBy,
        CancellationToken cancellationToken = default);

    /// <summary>Activa/desactiva el banner (AC3). Devuelve false si no existe o esta eliminado (AC4).</summary>
    Task<bool> SetActiveAsync(
        Guid id,
        bool isActive,
        Guid? updatedBy,
        CancellationToken cancellationToken = default);

    /// <summary>Soft-delete (deleted_at/deleted_by). Devuelve false si ya no existe (AC4).</summary>
    Task<bool> SoftDeleteAsync(
        Guid id,
        Guid? deletedBy,
        CancellationToken cancellationToken = default);
}
