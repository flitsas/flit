using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Catalogs;
using Flit.Tramites.Domain.Tramites.Catalog;
using Microsoft.EntityFrameworkCore;

namespace Flit.Infrastructure.Persistence.Repositories;

/// <summary>Catálogo de carrocerías desde <c>catalogs.vehicle_bodyworks</c>.</summary>
internal sealed class DbVehicleBodyworkCatalog : IVehicleBodyworkCatalog
{
    private readonly FlitDbContext _context;

    public DbVehicleBodyworkCatalog(FlitDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task<IReadOnlyList<VehicleBodyworkEntry>> SearchAsync(
        string? vehicleClass,
        string? term,
        int limit = 200,
        CancellationToken cancellationToken = default)
    {
        var take = Math.Clamp(limit, 1, 300);
        var rows = await _context.Set<VehicleBodywork>()
            .AsNoTracking()
            .Where(c => c.IsActive)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        IEnumerable<VehicleBodywork> scoped;
        if (string.IsNullOrWhiteSpace(vehicleClass))
        {
            scoped = rows.Where(c => string.IsNullOrWhiteSpace(c.ClassVehicle));
        }
        else
        {
            var known = rows
                .Select(c => c.ClassVehicle)
                .Where(c => !string.IsNullOrWhiteSpace(c))
                .Cast<string>()
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            var matched = VehicleClassCatalogFilter.MatchKnownClass(vehicleClass, known);
            if (matched is null)
                return [];

            var matchedNorm = VehicleClassCatalogFilter.Normalize(matched);
            scoped = rows.Where(c =>
                VehicleClassCatalogFilter.Normalize(c.ClassVehicle) == matchedNorm);
        }

        if (!string.IsNullOrWhiteSpace(term))
        {
            var t = term.Trim();
            scoped = scoped.Where(c =>
                c.Name.Contains(t, StringComparison.OrdinalIgnoreCase)
                || c.Code.Contains(t, StringComparison.OrdinalIgnoreCase));
        }

        return scoped
            .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(c => c.Code, StringComparer.OrdinalIgnoreCase)
            .Take(take)
            .Select(c => new VehicleBodyworkEntry(c.Id, c.Code, c.Name, c.ClassVehicle))
            .ToList();
    }
}
