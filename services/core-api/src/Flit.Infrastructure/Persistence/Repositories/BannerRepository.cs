using Flit.Admin.Domain.Banners;
using Microsoft.EntityFrameworkCore;

namespace Flit.Infrastructure.Persistence.Repositories;

/// <summary>
/// Lectura de <c>admin.banners</c> (Feature #12236, HU #12240). Sin filtro de tenant
/// (ADR-0058: tabla global); todas las consultas excluyen borrado logico
/// (<c>deleted_at IS NULL</c>).
/// </summary>
internal sealed class BannerRepository : IBannerRepository
{
    private readonly FlitDbContext _context;

    public BannerRepository(FlitDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
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
}
