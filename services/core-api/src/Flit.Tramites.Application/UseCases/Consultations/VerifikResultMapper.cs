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
            MapTecnomecanica(response.Data?.TecnoMecanica),
            MapGravamenes(info),
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
        // RUNT real: soat es un array. Vacío → unknown (sin información),
        // algún item VIGENTE → ok, todos vencidos/otros → fail.
        if (soat is null || soat.Count == 0)
            return new ConsultationCheck("soat", "SOAT", Unknown, Provider, "Sin SOAT registrado");

        var vigente = soat.Any(s => string.Equals(s?.Estado, "VIGENTE", StringComparison.OrdinalIgnoreCase));
        return new ConsultationCheck(
            "soat",
            "SOAT",
            vigente ? Ok : Fail,
            Provider,
            vigente ? null : "SOAT vencido o no vigente");
    }

    private static ConsultationCheck MapTecnomecanica(List<VerifikTecnomecanica>? tecno)
    {
        // RUNT real: tecnoMecanica es un array (puede venir vacío). Tomamos la revisión
        // vigente="SI" si existe; si no, evaluamos la forma del resto.
        if (tecno is null || tecno.Count == 0)
            return new ConsultationCheck("tecnomecanica", "Revisión técnico-mecánica", Unknown, Provider, "Sin información de tecnomecánica");

        if (tecno.Any(t => string.Equals(t?.Vigente, "SI", StringComparison.OrdinalIgnoreCase)))
            return new ConsultationCheck("tecnomecanica", "Revisión técnico-mecánica", Ok, Provider, null);

        if (tecno.All(t => string.Equals(t?.Vigente, "NO APLICA", StringComparison.OrdinalIgnoreCase)))
            return new ConsultationCheck("tecnomecanica", "Revisión técnico-mecánica", Unknown, Provider, "No aplica para este vehículo");

        if (tecno.Any(t => string.Equals(t?.Vigente, "NO", StringComparison.OrdinalIgnoreCase)))
            return new ConsultationCheck("tecnomecanica", "Revisión técnico-mecánica", Fail, Provider, "Tecnomecánica no vigente");

        // Items sin señal clara de vigencia → unknown.
        return new ConsultationCheck("tecnomecanica", "Revisión técnico-mecánica", Unknown, Provider, "Sin información de tecnomecánica");
    }

    private static ConsultationCheck MapGravamenes(VerifikInformacionGeneral? info)
    {
        // RUNT real: la señal de gravámenes vive en informacionGeneral.tieneGravamenes/prendas
        // (strings "SI"/"NO"), no en el array garantiasMobiliarias.
        if (info is null || (string.IsNullOrWhiteSpace(info.TieneGravamenes) && string.IsNullOrWhiteSpace(info.Prendas)))
            return new ConsultationCheck("gravamenes", "Gravámenes y limitaciones", Unknown, Provider, "Sin información de gravámenes");

        var sinGravamenes = !IsSi(info.TieneGravamenes);
        var sinPrendas = !IsSi(info.Prendas);

        if (sinGravamenes && sinPrendas)
            return new ConsultationCheck("gravamenes", "Gravámenes y limitaciones", Ok, Provider, null);

        return new ConsultationCheck("gravamenes", "Gravámenes y limitaciones", Warn, Provider, "El vehículo tiene gravámenes o prendas");
    }

    private static bool IsSi(string? value) =>
        string.Equals(value, "SI", StringComparison.OrdinalIgnoreCase);

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
