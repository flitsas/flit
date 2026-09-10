using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Catalogs;
using Flit.Tramites.Domain.Tramites.Catalog;
using Microsoft.EntityFrameworkCore;

namespace Flit.Infrastructure.Persistence.Repositories;

/// <summary>Catálogo de tipos de servicio del vehículo desde <c>catalogs.vehicle_service_types</c>.</summary>
internal sealed class DbVehicleServiceTypeCatalog : IVehicleServiceTypeCatalog
{
    private readonly FlitDbContext _context;

    public DbVehicleServiceTypeCatalog(FlitDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task<IReadOnlyList<VehicleServiceTypeEntry>> ListActiveAsync(
        CancellationToken cancellationToken = default) =>
        await _context.Set<VehicleServiceType>()
            .AsNoTracking()
            .Where(t => t.IsActive)
            .OrderBy(t => t.SortOrder)
            .Select(t => new VehicleServiceTypeEntry(t.Id, t.Code, t.Name, t.SortOrder))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
}
