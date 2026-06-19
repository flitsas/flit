namespace Flit.Tramites.Application.UseCases.Consultations;

/// <summary>
/// Mapper puro Verifik RUNT vehicle → <see cref="ConsultationResult"/> normalizado.
/// Robusto ante nulls/listas vacías: nunca lanza. Los literales de status y overall
/// son contrato estable con el frontend.
/// </summary>
public static class VerifikResultMapper
{
    private const string Provider = "verifik";

    // Status
    private const string Ok = "ok";
    private const string Warn = "warn";
    private const string Fail = "fail";
    private const string Unknown = "unknown";

    // Overall
    private const string Green = "green";
    private const string Yellow = "yellow";
    private const string Red = "red";

    public static ConsultationResult MapVehicle(VerifikVehicleResponse response)
    {
        var info = response.Data?.InformacionGeneral;

        var checks = new List<ConsultationCheck>
        {
            MapEstadoVehiculo(info),
            MapSoat(response.Data?.Soat),
            MapTecnomecanica(response.Data?.RevisionTecnomecanica),
            MapGravamenes(response.Data?.GarantiasMobiliarias),
        };

        var hydrated = MapHydratedFields(info);
        var overall = ComputeOverall(checks);

        return new ConsultationResult(Provider, overall, checks, hydrated);
    }

    private static ConsultationCheck MapEstadoVehiculo(VerifikInformacionGeneral? info)
    {
        var estado = info?.EstadoDelVehiculo;
        if (string.IsNullOrWhiteSpace(estado))
            return new ConsultationCheck("estado_vehiculo", "Estado del vehículo", Unknown, Provider, "Sin información de estado");

        var isActivo = string.Equals(estado, "ACTIVO", StringComparison.OrdinalIgnoreCase);
        return new ConsultationCheck(
            "estado_vehiculo",
            "Estado del vehículo",
            isActivo ? Ok : Fail,
            Provider,
            isActivo ? null : $"Estado: {estado}");
    }

    private static ConsultationCheck MapSoat(List<VerifikSoat>? soat)
    {
        if (soat is null || soat.Count == 0)
            return new ConsultationCheck("soat", "SOAT", Fail, Provider, "Sin SOAT registrado");

        var vigente = soat.Any(s => string.Equals(s?.Estado, "VIGENTE", StringComparison.OrdinalIgnoreCase));
        return new ConsultationCheck(
            "soat",
            "SOAT",
            vigente ? Ok : Fail,
            Provider,
            vigente ? null : "SOAT vencido o no vigente");
    }

    private static ConsultationCheck MapTecnomecanica(VerifikTecnomecanica? tecno)
    {
        var vigente = tecno?.Vigente;
        if (string.IsNullOrWhiteSpace(vigente))
            return new ConsultationCheck("tecnomecanica", "Revisión técnico-mecánica", Unknown, Provider, "Sin información de tecnomecánica");

        if (string.Equals(vigente, "SI", StringComparison.OrdinalIgnoreCase))
            return new ConsultationCheck("tecnomecanica", "Revisión técnico-mecánica", Ok, Provider, null);

        if (string.Equals(vigente, "NO APLICA", StringComparison.OrdinalIgnoreCase))
            return new ConsultationCheck("tecnomecanica", "Revisión técnico-mecánica", Unknown, Provider, "No aplica para este vehículo");

        // "NO" u otros → fail
        return new ConsultationCheck("tecnomecanica", "Revisión técnico-mecánica", Fail, Provider, "Tecnomecánica no vigente");
    }

    private static ConsultationCheck MapGravamenes(VerifikGravamenes? grav)
    {
        if (grav is null)
            return new ConsultationCheck("gravamenes", "Gravámenes y limitaciones", Unknown, Provider, "Sin información de gravámenes");

        var sinGravamenes = IsNo(grav.TieneGravamenes);
        var sinPrendas = IsNo(grav.Prendas);
        var sinLimitacion = string.IsNullOrWhiteSpace(grav.LimitacionPropiedad);

        if (sinGravamenes && sinPrendas && sinLimitacion)
            return new ConsultationCheck("gravamenes", "Gravámenes y limitaciones", Ok, Provider, null);

        return new ConsultationCheck("gravamenes", "Gravámenes y limitaciones", Warn, Provider, "El vehículo tiene gravámenes, prendas o limitaciones");
    }

    private static bool IsNo(string? value) =>
        string.Equals(value, "NO", StringComparison.OrdinalIgnoreCase);

    private static List<HydratedField> MapHydratedFields(VerifikInformacionGeneral? info)
    {
        if (info is null)
            return [];

        var fields = new List<HydratedField>();

        if (!string.IsNullOrWhiteSpace(info.NoPlaca))
            fields.Add(new HydratedField("plate", info.NoPlaca, null));

        if (!string.IsNullOrWhiteSpace(info.NoVin))
            fields.Add(new HydratedField("vin", info.NoVin, null));

        if (!string.IsNullOrWhiteSpace(info.Modelo))
            fields.Add(new HydratedField("vehicle_year", info.Modelo, null));

        return fields;
    }

    private static string ComputeOverall(IReadOnlyList<ConsultationCheck> checks)
    {
        if (checks.Any(c => c.Status == Fail))
            return Red;

        if (checks.Any(c => c.Status == Warn))
            return Yellow;

        // Sin fail ni warn: green si hay al menos un ok; yellow si solo hay unknown.
        if (checks.Any(c => c.Status == Ok))
            return Green;

        return Yellow;
    }
}
