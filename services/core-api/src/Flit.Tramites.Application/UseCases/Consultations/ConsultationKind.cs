namespace Flit.Tramites.Application.UseCases.Consultations;

/// <summary>
/// Tipo de consulta que resuelve la cadena de proveedores (HU #10478). Cada uno tiene su propia
/// cadena Kyverum-first con fallback a Verifik (ver <see cref="ConsultationChainOptions"/>).
/// </summary>
public enum ConsultationKind
{
    /// <summary>Vehículo por VIN (matrícula inicial).</summary>
    VehicleVin,

    /// <summary>Vehículo por placa + documento del propietario (traspaso).</summary>
    VehiclePlate,

    /// <summary>Persona / conductor por documento.</summary>
    Conductor,
}

/// <summary>Claves de configuración estables por <see cref="ConsultationKind"/> (contrato con appsettings).</summary>
public static class ConsultationKindKeys
{
    public const string VehicleVin = "vehicle_vin";
    public const string VehiclePlate = "vehicle_plate";
    public const string Conductor = "conductor";

    public static string ToConfigKey(this ConsultationKind kind) => kind switch
    {
        ConsultationKind.VehicleVin => VehicleVin,
        ConsultationKind.VehiclePlate => VehiclePlate,
        ConsultationKind.Conductor => Conductor,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "ConsultationKind no soportado"),
    };
}
