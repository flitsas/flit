using Flit.Admin.Domain.Common;

namespace Flit.Admin.Domain.Banners;

/// <summary>
/// Repositorio de banners promocionales (Feature #12236, HU #12239/#12240) sobre
/// <c>admin.banners</c> - tabla GLOBAL sin <c>tenant_id</c> ni RLS (excepcion documentada en
/// ADR-0058). Ninguna consulta filtra por tenant. El soft-delete estandar
/// (<c>deleted_at IS NULL</c>) si aplica. La implementacion EF Core vive en Flit.Infrastructure
/// (<c>BannerRepository</c>).
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
    /// el ETag que sirve HU #12240). Devuelve null si no existe o esta eliminado (AC4).
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

    /// <summary>
    /// Banners visibles para consumo publico (HU #12240, AC1): <c>is_active = true</c> y, si
    /// tiene vigencia programada, <paramref name="nowUtc"/> dentro de [valid_from, valid_until].
    /// Un banner sin vigencia programada (ambas columnas NULL) aparece siempre que este activo.
    /// Un banner Programado (valid_from aun no llega) o Expirado (valid_until ya paso) queda
    /// excluido aunque is_active sea true.
    /// <para><paramref name="nowUtc"/> es un INSTANTE (UTC). Colombia (America/Bogota) tiene
    /// offset fijo -05:00 sin horario de verano, asi que comparar el instante UTC contra
    /// columnas <c>timestamptz</c> es equivalente a comparar en hora de Bogota — mismo
    /// argumento que <c>Flit.Infrastructure.Persistence.Repositories.BogotaDays</c> aplica a
    /// rangos de dia calendario. No hace falta convertir el reloj: alcanza con no usar hora
    /// local del servidor.</para>
    /// </summary>
    Task<IReadOnlyList<ActiveBannerItem>> ListActiveAsync(
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Referencia de storage de la imagen de un banner NO borrado logicamente (HU #12240,
    /// AC2/AC3). Devuelve <c>null</c> si el banner no existe o esta soft-deleted — el caller
    /// responde 404 sin distinguir el motivo (evita enumeracion).
    /// </summary>
    Task<BannerImageRef?> GetImageRefAsync(Guid id, CancellationToken cancellationToken = default);
}
