namespace Flit.Admin.Domain.Companies.VehicleOwnership;

/// <summary>
/// Interceptor de propiedad vehicular (HU #10191, RF04). Valida, antes de iniciar
/// un traspaso, la regla <c>only_own_vehicles</c> del tenant, contemplando las
/// excepciones de la lista blanca (RF05).
/// </summary>
public interface IVehicleOwnershipGuard
{
    /// <summary>
    /// Evalúa si el traspaso descrito en <paramref name="context"/> puede iniciarse.
    /// </summary>
    Task<VehicleOwnershipGuardResult> ValidateTransferStartAsync(
        VehicleOwnershipCheckContext context,
        CancellationToken cancellationToken = default);
}
