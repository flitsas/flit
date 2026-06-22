using Flit.Admin.Domain.Companies.VehicleOwnership;

namespace Flit.Infrastructure.Persistence.Repositories;

/// <summary>
/// Implementación transitoria de <see cref="IVehicleTenantOwnershipChecker"/>
/// (HU #10191). El módulo de vehículos/traspasos todavía no existe como EF, por lo
/// que esta comprobación es un placeholder conservador: trata todo vehículo como
/// <em>no</em> propiedad del tenant hasta que llegue el módulo real. Con
/// <c>only_own_vehicles=true</c> esto bloquea el traspaso salvo exención por lista
/// blanca (RF05). El comportamiento real se sustituye en tests y se reemplazará al
/// integrar el módulo de vehículos.
/// </summary>
internal sealed class StubVehicleTenantOwnershipChecker : IVehicleTenantOwnershipChecker
{
    public Task<bool> IsVehicleOwnedByTenantAsync(
        Guid tenantId,
        string vehicleIdentifier,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(false);
}
