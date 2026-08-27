namespace Flit.Tramites.Domain.Tramites.Catalog;

/// <summary>Entrada del catálogo persistido de carrocerías.</summary>
public sealed record VehicleBodyworkEntry(Guid Id, string Code, string Name, string? ClassVehicle);

/// <summary>Catálogo RUNT de carrocerías (<c>catalogs.vehicle_bodyworks</c>).</summary>
public interface IVehicleBodyworkCatalog
{
    /// <summary>
    /// Lista carrocerías activas. Con clase de vehículo: solo las de esa clase.
    /// Sin clase: solo filas de respaldo (<c>class_vehicle</c> nulo).
    /// </summary>
    Task<IReadOnlyList<VehicleBodyworkEntry>> SearchAsync(
        string? vehicleClass,
        string? term,
        int limit = 200,
        CancellationToken cancellationToken = default);
}
