using Flit.Admin.Domain.Banners;
using Flit.Admin.Domain.Common;
using Flit.Infrastructure.Persistence.Entities.Admin;
using Microsoft.EntityFrameworkCore;

namespace Flit.Infrastructure.Persistence.Repositories;

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
