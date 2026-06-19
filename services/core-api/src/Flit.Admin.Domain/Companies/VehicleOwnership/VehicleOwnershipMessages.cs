namespace Flit.Admin.Domain.Companies.VehicleOwnership;

/// <summary>
/// Mensajes de dominio del interceptor de propiedad vehicular (HU #10191, RF04).
/// </summary>
public static class VehicleOwnershipMessages
{
    /// <summary>
    /// Error exacto del AC1 cuando el vehículo no pertenece a la compañía jurídica
    /// del tenant y la regla <c>only_own_vehicles</c> está activa.
    /// </summary>
    public const string VehicleNotOwned = "Vehículo no es propiedad de la compañía jurídica del tenant";
}
