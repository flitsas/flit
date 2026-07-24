using Flit.Admin.Domain.Common;
using Flit.Admin.Domain.Companies.LegalRepresentatives;
using Flit.Infrastructure.Persistence.Entities.Admin;
using Microsoft.EntityFrameworkCore;

namespace Flit.Infrastructure.Persistence.Repositories;

/// <summary>
/// Lecturas EF Core de escrituras (HU #10900, ADR-0033). Tenant-scoped: corre bajo el contexto RLS
/// del tenant (<see cref="TenantRlsScope"/>). Proyecta las compañías (puente M:N) sin N+1.
/// </summary>
internal sealed class DbDeedReader : IDeedReader
{
    private readonly FlitDbContext _context;

    public DbDeedReader(FlitDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public Task<PagedResult<DeedItem>> ListPagedAsync(
        Guid tenantId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default) =>
        TenantRlsScope.ExecuteAsync(
            _context,
            tenantId,
            async () =>
            {
                var baseQuery = _context.CompanyDeeds
                    .AsNoTracking()
                    .Where(d => d.TenantId == tenantId);

                var totalCount = await baseQuery.LongCountAsync(cancellationToken).ConfigureAwait(false);
                if (totalCount == 0)
                {
                    return PagedResult<DeedItem>.Empty;
                }

                var rows = await baseQuery
                    .OrderByDescending(d => d.CreatedAt)
                    .ThenByDescending(d => d.Id)
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);

                var items = await ProjectAsync(rows, cancellationToken).ConfigureAwait(false);
                return new PagedResult<DeedItem>(items, totalCount);
            },
            cancellationToken);

    public Task<DeedItem?> GetByIdAsync(
        Guid tenantId,
        Guid id,
        CancellationToken cancellationToken = default) =>
        TenantRlsScope.ExecuteAsync(
            _context,
            tenantId,
            async () =>
            {
                var row = await _context.CompanyDeeds
                    .AsNoTracking()
                    .FirstOrDefaultAsync(d => d.TenantId == tenantId && d.Id == id, cancellationToken)
                    .ConfigureAwait(false);

                if (row is null)
                {
                    return null;
                }

                var items = await ProjectAsync([row], cancellationToken).ConfigureAwait(false);
                return items.Count == 0 ? null : items[0];
            },
            cancellationToken);

    public Task<IReadOnlyList<DeedItem>> ListActiveVigentesAsync(
        Guid tenantId,
        DateOnly today,
        CancellationToken cancellationToken = default) =>
        TenantRlsScope.ExecuteAsync(
            _context,
            tenantId,
            async () =>
            {
                var rows = await _context.CompanyDeeds
                    .AsNoTracking()
                    .Where(d => d.TenantId == tenantId
                        && d.IsActive
                        && d.VigenciaDesde <= today
                        && d.VigenciaHasta >= today)
                    .OrderBy(d => d.VigenciaHasta)
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);

                return await ProjectAsync(rows, cancellationToken).ConfigureAwait(false);
            },
            cancellationToken);

    private async Task<IReadOnlyList<DeedItem>> ProjectAsync(
        List<CompanyDeedEntity> rows,
        CancellationToken cancellationToken)
    {
        if (rows.Count == 0)
        {
            return [];
        }

        var deedIds = rows.Select(d => d.Id).ToList();
        var companyRows = await _context.CompanyDeedCompanies
            .AsNoTracking()
            .Where(dc => deedIds.Contains(dc.DeedId))
            .Select(dc => new { dc.DeedId, dc.RepresentedCompanyId })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var companiesByDeed = companyRows
            .GroupBy(dc => dc.DeedId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<Guid>)[.. g.Select(x => x.RepresentedCompanyId)]);

        return [.. rows.Select(d =>
        {
            companiesByDeed.TryGetValue(d.Id, out var companyIds);
            return new DeedItem
            {
                Id = d.Id,
                TenantId = d.TenantId,
                Description = d.Description,
                StoragePath = d.StoragePath,
                StorageSha256 = d.StorageSha256,
                VigenciaDesde = d.VigenciaDesde,
                VigenciaHasta = d.VigenciaHasta,
                IsActive = d.IsActive,
                RepresentedCompanyIds = companyIds ?? [],
                CreatedAt = d.CreatedAt,
                UpdatedAt = d.UpdatedAt,
            };
        })];
    }
}
