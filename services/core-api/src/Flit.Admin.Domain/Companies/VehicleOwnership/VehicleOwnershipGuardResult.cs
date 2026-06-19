namespace Flit.Admin.Domain.Companies.VehicleOwnership;

/// <summary>
/// Resultado de la evaluación del interceptor (HU #10191, RF04). Cuando
/// <see cref="IsAllowed"/> es falso, <see cref="Error"/> contiene el mensaje exacto
/// que el endpoint traduce a HTTP 422 (AC1).
/// </summary>
/// <param name="IsAllowed">Si el traspaso puede continuar.</param>
/// <param name="Error">Mensaje de bloqueo cuando no se permite; <c>null</c> si se permite.</param>
public sealed record VehicleOwnershipGuardResult(bool IsAllowed, string? Error)
{
    /// <summary>El traspaso puede continuar.</summary>
    public static VehicleOwnershipGuardResult Allowed() => new(true, null);

    /// <summary>El traspaso se detiene con <paramref name="error"/>.</summary>
    public static VehicleOwnershipGuardResult Blocked(string error) => new(false, error);
}
