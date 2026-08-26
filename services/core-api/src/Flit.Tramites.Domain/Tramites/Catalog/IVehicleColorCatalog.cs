namespace Flit.Tramites.Domain.Tramites.Catalog;

/// <summary>Entrada del catálogo persistido de colores de vehículo.</summary>
public sealed record VehicleColorEntry(Guid Id, string Code, string Name);

/// <summary>Catálogo RUNT de colores (<c>catalogs.vehicle_colors</c>) con búsqueda acotada.</summary>
public interface IVehicleColorCatalog
{
    /// <summary>
    /// Busca colores activos por código o nombre. Sin término devuelve los primeros
    /// <paramref name="limit"/> ordenados por nombre (para el dropdown inicial).
    /// </summary>
    Task<IReadOnlyList<VehicleColorEntry>> SearchAsync(
        string? term,
        int limit = 50,
        CancellationToken cancellationToken = default);
}
