using Flit.Admin.Domain.Common;
using Flit.Admin.Domain.Companies;
using Flit.Infrastructure.Persistence.Entities.Identity;
using Microsoft.EntityFrameworkCore;

namespace Flit.Infrastructure.Persistence.Repositories;

/// <summary>
/// Implementación EF Core del listado administrativo de compañías (HU #10189).
///
/// Decisión de fuente de datos: el listado se construye exclusivamente sobre
/// <c>identity.tenants</c> (sin RLS). No se hace join a <c>admin.tenant_profiles</c>
/// (trade_name) porque esa tabla tiene Row Level Security por tenant; una lectura
/// cross-tenant de SuperAdmin requeriría un rol de BD privilegiado / bypass de RLS.
/// Se difiere a una HU posterior; la Razón Social se toma de <c>legal_name</c>
/// según el mapeo del handoff.
///
/// Paginación offset (Skip/Take) sobre índice de <c>created_at</c>: válida para el
/// MVP. Para escala 1M+ evaluar keyset pagination en una HU futura.
/// </summary>
internal sealed class CompanyReadRepository : ICompanyReadRepository
{
    private readonly FlitDbContext _context;

    public CompanyReadRepository(FlitDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task<PagedResult<CompanyListItem>> ListAsync(
        CompanyListFilter filter,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);

        var query = ApplyFilters(_context.Tenants.AsNoTracking(), filter);

        var totalCount = await query.LongCountAsync(cancellationToken).ConfigureAwait(false);

        if (totalCount == 0)
        {
            return PagedResult<CompanyListItem>.Empty;
        }

        var items = await query
            .OrderByDescending(t => t.CreatedAt)
            .ThenByDescending(t => t.Id)
            .Skip((filter.Page - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .Select(t => new CompanyListItem
            {
                Id = t.Id,
                Nit = t.TaxId,
                RazonSocial = t.LegalName,
                EstadoActivo = t.IsActive,
                FechaCreacion = t.CreatedAt,
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return new PagedResult<CompanyListItem>(items, totalCount);
    }

    private static IQueryable<Tenant> ApplyFilters(IQueryable<Tenant> query, CompanyListFilter filter)
    {
        if (!string.IsNullOrWhiteSpace(filter.Nit))
        {
            var nit = filter.Nit.Trim();
            query = query.Where(t => t.TaxId.Contains(nit));
        }

        if (!string.IsNullOrWhiteSpace(filter.RazonSocial))
        {
            var razon = filter.RazonSocial.Trim().ToLowerInvariant();
            query = query.Where(t => t.LegalName.ToLowerInvariant().Contains(razon));
        }

        if (filter.EstadoActivo is { } estado)
        {
            query = query.Where(t => t.IsActive == estado);
        }

        if (filter.FechaDesde is { } desde)
        {
            var desdeOffset = new DateTimeOffset(desde.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            query = query.Where(t => t.CreatedAt >= desdeOffset);
        }

        if (filter.FechaHasta is { } hasta)
        {
            // Inclusivo del día completo: < día siguiente a las 00:00 UTC.
            var hastaOffset = new DateTimeOffset(hasta.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero).AddDays(1);
            query = query.Where(t => t.CreatedAt < hastaOffset);
        }

        return query;
    }
}
