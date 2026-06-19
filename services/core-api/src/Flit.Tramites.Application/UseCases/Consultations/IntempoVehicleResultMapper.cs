namespace Flit.Tramites.Application.UseCases.Consultations;

/// <summary>
/// Mapper puro INTEMPO vehículo (§4.1 VIN / §4.2 PLACA) → <see cref="ConsultationResult"/> normalizado.
/// Aplica la misma lógica de semáforo que Verifik RUNT, sobre los campos INTEMPO:
///   soatNacionales[].estado, tieneGravamenes/prendas, estadoDelVehiculo.
/// Todos los escalares booleanos son strings "SI"/"NO" (quirk INTEMPO documentado).
/// Nunca lanza; robusto ante nulls.
/// </summary>
public static class IntempoVehicleResultMapper
{
    private const string Provider = "intempo";

    private const string Ok = "ok";
    private const string Warn = "warn";
    private const string Fail = "fail";
    private const string Unknown = "unknown";

    private const string Green = "green";
    private const string Yellow = "yellow";
    private const string Red = "red";

    public static ConsultationResult Map(IntempoVehicleResponse response)
    {
        // codigoResultado="Error" → error de negocio (VIN/placa no encontrado).
        if (string.Equals(response.CodigoResultado, "Error", StringComparison.OrdinalIgnoreCase))
        {
            return new ConsultationResult(
                Provider,
                Red,
                [new ConsultationCheck("vehiculo", "Vehículo INTEMPO", Fail, Provider, "Vehículo no encontrado")],
                []);
        }

        var checks = new List<ConsultationCheck>
        {
            MapEstadoVehiculo(response.EstadoDelVehiculo),
            MapSoat(response.SoatNacionales),
            MapGravamenes(response.TieneGravamenes, response.Prendas, response.LimitacionesPropiedad),
        };

        var hydrated = MapHydratedFields(response);
        var overall = ComputeOverall(checks);

        return new ConsultationResult(Provider, overall, checks, hydrated);
    }

    private static ConsultationCheck MapEstadoVehiculo(string? estado)
    {
        if (string.IsNullOrWhiteSpace(estado))
            return new ConsultationCheck("estado_vehiculo", "Estado del vehículo", Unknown, Provider, "Sin información de estado");

        var activo = string.Equals(estado, "ACTIVO", StringComparison.OrdinalIgnoreCase);
        return new ConsultationCheck(
            "estado_vehiculo",
            "Estado del vehículo",
            activo ? Ok : Fail,
            Provider,
            activo ? null : $"Estado: {estado}");
    }

    private static ConsultationCheck MapSoat(List<IntempoSoat>? soat)
    {
        if (soat is null || soat.Count == 0)
            return new ConsultationCheck("soat", "SOAT", Fail, Provider, "Sin SOAT registrado");

        var vigente = soat.Any(s => string.Equals(s.Estado, "VIGENTE", StringComparison.OrdinalIgnoreCase));
        return new ConsultationCheck(
            "soat",
            "SOAT",
            vigente ? Ok : Fail,
            Provider,
            vigente ? null : "SOAT vencido o no vigente");
    }

    private static ConsultationCheck MapGravamenes(
        string? tieneGravamenes,
        string? prendas,
        List<object>? limitaciones)
    {
        var sinGravamenes = IsNo(tieneGravamenes);
        var sinPrendas = IsNo(prendas);
        var sinLimitaciones = limitaciones is null || limitaciones.Count == 0;

        if (sinGravamenes && sinPrendas && sinLimitaciones)
            return new ConsultationCheck("gravamenes", "Gravámenes y limitaciones", Ok, Provider, null);

        return new ConsultationCheck(
            "gravamenes",
            "Gravámenes y limitaciones",
            Warn,
            Provider,
            "El vehículo tiene gravámenes, prendas o limitaciones");
    }

    private static bool IsNo(string? value) =>
        string.Equals(value, "NO", StringComparison.OrdinalIgnoreCase);

    private static List<HydratedField> MapHydratedFields(IntempoVehicleResponse r)
    {
        var fields = new List<HydratedField>();

        if (!string.IsNullOrWhiteSpace(r.NoPlaca))
            fields.Add(new HydratedField("plate", r.NoPlaca, null));

        if (!string.IsNullOrWhiteSpace(r.NoVin))
            fields.Add(new HydratedField("vin", r.NoVin, null));

        if (!string.IsNullOrWhiteSpace(r.Modelo))
            fields.Add(new HydratedField("vehicle_year", r.Modelo, null));

        if (!string.IsNullOrWhiteSpace(r.Marca))
            fields.Add(new HydratedField("vehicle_brand", r.Marca, null));

        if (!string.IsNullOrWhiteSpace(r.Linea))
            fields.Add(new HydratedField("vehicle_line", r.Linea, null));

        return fields;
    }

    private static string ComputeOverall(IReadOnlyList<ConsultationCheck> checks)
    {
        if (checks.Any(c => c.Status == Fail)) return Red;
        if (checks.Any(c => c.Status == Warn)) return Yellow;
        if (checks.Any(c => c.Status == Ok)) return Green;
        return Yellow;
    }
}
