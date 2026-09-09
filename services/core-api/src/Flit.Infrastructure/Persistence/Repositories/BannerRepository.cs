using Flit.Admin.Domain.Banners;
using Flit.Admin.Domain.Common;
using Flit.Infrastructure.Persistence.Entities.Admin;
using Microsoft.EntityFrameworkCore;

namespace Flit.Infrastructure.Persistence.Repositories;

/// <summary>
/// Acceso a <c>admin.banners</c> (Feature #12236, HU #12239/#12240). Sin filtro de tenant
/// (ADR-0058: tabla global); todas las consultas excluyen borrado logico
/// (<c>deleted_at IS NULL</c>) salvo donde se indique lo contrario.
/// </summary>
internal sealed class BannerRepository : IBannerRepository
{
    private readonly FlitDbContext _context;

    public BannerRepository(FlitDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task<BannerListItem> CreateAsync(
        string name,
        string imageStoragePath,
        string imageSha256,
        string? linkUrl,
        DateTimeOffset? validFrom,
        DateTimeOffset? validUntil,
        Guid? createdBy,
        CancellationToken cancellationToken = default)
    {
        var entity = new Banner
        {
            Id = Guid.NewGuid(),
            Name = name,
            ImageStoragePath = imageStoragePath,
            ImageSha256 = imageSha256,
            LinkUrl = linkUrl,
            ValidFrom = validFrom,
            ValidUntil = validUntil,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedBy = createdBy,
        };

        _context.Banners.Add(entity);
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Map(entity);
    }

    public async Task<PagedResult<BannerListItem>> ListAsync(
        BannerListFilter filter,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);

        var query = _context.Banners.AsNoTracking();

        if (!filter.IncludeDeleted)
        {
            query = query.Where(b => b.DeletedAt == null);
        }

        var totalCount = await query.LongCountAsync(cancellationToken).ConfigureAwait(false);

        if (totalCount == 0)
        {
            return PagedResult<BannerListItem>.Empty;
        }

        var items = await query
            .OrderByDescending(b => b.CreatedAt)
            .ThenByDescending(b => b.Id)
            .Skip((filter.Page - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .Select(b => new BannerListItem
            {
                Id = b.Id,
                Name = b.Name,
                ImageStoragePath = b.ImageStoragePath,
                ImageSha256 = b.ImageSha256,
                LinkUrl = b.LinkUrl,
                ValidFrom = b.ValidFrom,
                ValidUntil = b.ValidUntil,
                IsActive = b.IsActive,
                CreatedAt = b.CreatedAt,
                UpdatedAt = b.UpdatedAt,
                RowVersion = b.RowVersion,
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return new PagedResult<BannerListItem>(items, totalCount);
    }

    public async Task<BannerListItem?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var entity = await _context.Banners
            .AsNoTracking()
            .FirstOrDefaultAsync(b => b.Id == id && b.DeletedAt == null, cancellationToken)
            .ConfigureAwait(false);

        return entity is null ? null : Map(entity);
    }

    public async Task<BannerListItem?> UpdateAsync(
        Guid id,
        string name,
        string? linkUrl,
        DateTimeOffset? validFrom,
        DateTimeOffset? validUntil,
        string? imageStoragePath,
        string? imageSha256,
        Guid? updatedBy,
        CancellationToken cancellationToken = default)
    {
        var entity = await _context.Banners
            .FirstOrDefaultAsync(b => b.Id == id && b.DeletedAt == null, cancellationToken)
            .ConfigureAwait(false);

        if (entity is null)
        {
            return null;
        }

        entity.Name = name;
        entity.LinkUrl = linkUrl;
        entity.ValidFrom = validFrom;
        entity.ValidUntil = validUntil;
        entity.UpdatedAt = DateTimeOffset.UtcNow;
        entity.UpdatedBy = updatedBy;

        if (imageStoragePath is not null && imageSha256 is not null)
        {
            entity.ImageStoragePath = imageStoragePath;
            entity.ImageSha256 = imageSha256;
        }

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Map(entity);
    }

    public async Task<bool> SetActiveAsync(
        Guid id,
        bool isActive,
        Guid? updatedBy,
        CancellationToken cancellationToken = default)
    {
        var entity = await _context.Banners
            .FirstOrDefaultAsync(b => b.Id == id && b.DeletedAt == null, cancellationToken)
            .ConfigureAwait(false);

        if (entity is null)
        {
            return false;
        }

        entity.IsActive = isActive;
        entity.UpdatedAt = DateTimeOffset.UtcNow;
        entity.UpdatedBy = updatedBy;

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return true;
    }

    public async Task<bool> SoftDeleteAsync(
        Guid id,
        Guid? deletedBy,
        CancellationToken cancellationToken = default)
    {
        var entity = await _context.Banners
            .FirstOrDefaultAsync(b => b.Id == id && b.DeletedAt == null, cancellationToken)
            .ConfigureAwait(false);

        if (entity is null)
        {
            return false;
        }

        entity.DeletedAt = DateTimeOffset.UtcNow;
        entity.DeletedBy = deletedBy;

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return true;
    }

    public async Task<IReadOnlyList<ActiveBannerItem>> ListActiveAsync(
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        // Sin fechas programadas (ambas NULL) => siempre visible mientras is_active. Con
        // fechas, solo dentro de [valid_from, valid_until]: excluye Programado (aun no llega
        // valid_from) y Expirado (valid_until ya paso), aunque is_active siga en true.
        var items = await _context.Banners
            .AsNoTracking()
            .Where(b => b.DeletedAt == null && b.IsActive)
            .Where(b => (b.ValidFrom == null && b.ValidUntil == null)
                || (b.ValidFrom <= nowUtc && b.ValidUntil >= nowUtc))
            .OrderBy(b => b.CreatedAt)
            .ThenBy(b => b.Id)
            .Select(b => new ActiveBannerItem(b.Id, b.Name, b.LinkUrl))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return items;
    }

    public async Task<BannerImageRef?> GetImageRefAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var entity = await _context.Banners
            .AsNoTracking()
            .Where(b => b.Id == id && b.DeletedAt == null)
            .Select(b => new { b.ImageStoragePath, b.ImageSha256 })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return entity is null ? null : new BannerImageRef(entity.ImageStoragePath, entity.ImageSha256);
    }

    private static BannerListItem Map(Banner entity) => new()
    {
        Id = entity.Id,
        Name = entity.Name,
        ImageStoragePath = entity.ImageStoragePath,
        ImageSha256 = entity.ImageSha256,
        LinkUrl = entity.LinkUrl,
        ValidFrom = entity.ValidFrom,
        ValidUntil = entity.ValidUntil,
        IsActive = entity.IsActive,
        CreatedAt = entity.CreatedAt,
        UpdatedAt = entity.UpdatedAt,
        RowVersion = entity.RowVersion,
    };
}
