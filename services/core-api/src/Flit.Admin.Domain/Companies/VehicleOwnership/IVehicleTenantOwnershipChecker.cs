namespace Flit.Admin.Domain.Companies.VehicleOwnership;

/// <summary>
/// Comprueba si un vehículo pertenece a la compañía jurídica de un tenant
/// (HU #10191, RF04). El módulo de vehículos/traspasos aún no existe como EF, por
/// lo que esta interfaz se modela como contrato: la implementación real llegará con
/// ese módulo; en los tests se sustituye por un doble.
/// </summary>
public interface IVehicleTenantOwnershipChecker
{
    /// <summary>
    /// <c>true</c> si <paramref name="vehicleIdentifier"/> está registrado como
    /// propiedad de la compañía del <paramref name="tenantId"/>.
    /// </summary>
    Task<bool> IsVehicleOwnedByTenantAsync(
        Guid tenantId,
        string vehicleIdentifier,
        CancellationToken cancellationToken = default);
}
