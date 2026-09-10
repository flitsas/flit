namespace Flit.Tramites.Domain.Tramites.Catalog;

/// <summary>Entrada del catálogo persistido de tipos de servicio del vehículo.</summary>
public sealed record VehicleServiceTypeEntry(Guid Id, string Code, string Name, int SortOrder);

/// <summary>
/// Catálogo global (sección 18 del FUR) de tipos de servicio del vehículo
/// (<c>catalogs.vehicle_service_types</c>). Cerrado: 6 valores, sin búsqueda paginada.
/// </summary>
public interface IVehicleServiceTypeCatalog
{
    /// <summary>Lista los tipos activos, ordenados por <c>sort_order</c> (orden normativo del FUR).</summary>
    Task<IReadOnlyList<VehicleServiceTypeEntry>> ListActiveAsync(
        CancellationToken cancellationToken = default);
}
