using Flit.Tramites.Domain.Tramites.Services;

namespace Flit.Tramites.Application.UseCases.Consultations;

/// <summary>
/// Mapper puro Kyverum RUNT vehicle → <see cref="ConsultationResult"/> normalizado (HU #10478).
/// Kyverum-first: produce los MISMOS <c>Check.Key</c> e <c>HydratedField.FieldKey</c> que
/// <see cref="VerifikResultMapper"/> para que el frontend nunca distinga proveedor; solo cambia
/// <c>Source</c> = <c>kyverum_runt</c>. Robusto ante nulls/listas vacías: nunca lanza. Los literales
/// de status/overall son contrato estable con el frontend.
/// </summary>
public static class KyverumRuntVehicleResultMapper
{
    private const string Provider = "kyverum_runt";

    private const string Ok = "ok";
    private const string Warn = "warn";
    private const string Fail = "fail";
    private const string Unknown = "unknown";

    private const string Green = "green";
    private const string Yellow = "yellow";
    private const string Red = "red";

    public static ConsultationResult MapVehicle(KyverumRuntVehicleResponse response)
    {
        var vehiculo = response.Data?.Vehiculo;

        var checks = new List<ConsultationCheck>
        {
            MapEstadoVehiculo(vehiculo),
            MapSoat(response.Data?.Soat),
            MapTecnomecanica(response.Data?.Rtm),
            MapGravamenes(vehiculo),
        };

        var hydrated = MapHydratedFields(response.Data);
        var overall = ComputeOverall(checks);

        return new ConsultationResult(Provider, overall, checks, hydrated);
    }

    private static ConsultationCheck MapEstadoVehiculo(KyverumRuntVehiculo? vehiculo)
    {
        var estado = vehiculo?.EstadoAutomotor;
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

    private static ConsultationCheck MapSoat(List<KyverumRuntSoat>? soat)
    {
        // Array de pólizas. Vacío → unknown; alguna VIGENTE → ok; todas no vigentes → fail.
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

    private static ConsultationCheck MapTecnomecanica(List<KyverumRuntRtm>? rtm)
    {
        // Array de revisiones (histórico). Mismo criterio que Verifik sobre el campo "vigente":
        // vacío/ausente → unknown (muchos vehículos nuevos no tienen RTM aún); alguna "SI" → ok;
        // todas "NO APLICA" → unknown; alguna "NO" → fail (vencida); resto → unknown.
        if (rtm is null || rtm.Count == 0)
            return new ConsultationCheck("tecnomecanica", "Revisión técnico-mecánica", Unknown, Provider, "Sin información de tecnomecánica");

        if (rtm.Any(t => string.Equals(t?.Vigente, "SI", StringComparison.OrdinalIgnoreCase)))
            return new ConsultationCheck("tecnomecanica", "Revisión técnico-mecánica", Ok, Provider, null);

        if (rtm.All(t => string.Equals(t?.Vigente, "NO APLICA", StringComparison.OrdinalIgnoreCase)))
            return new ConsultationCheck("tecnomecanica", "Revisión técnico-mecánica", Unknown, Provider, "No aplica para este vehículo");

        if (rtm.Any(t => string.Equals(t?.Vigente, "NO", StringComparison.OrdinalIgnoreCase)))
            return new ConsultationCheck("tecnomecanica", "Revisión técnico-mecánica", Fail, Provider, "Tecnomecánica no vigente");

        return new ConsultationCheck("tecnomecanica", "Revisión técnico-mecánica", Unknown, Provider, "Sin información de tecnomecánica");
    }

    private static ConsultationCheck MapGravamenes(KyverumRuntVehiculo? vehiculo)
    {
        // Señal de gravámenes/prendas en el propio vehículo (strings "SI"/"NO").
        if (vehiculo is null || (string.IsNullOrWhiteSpace(vehiculo.Gravamenes) && string.IsNullOrWhiteSpace(vehiculo.Prendas)))
            return new ConsultationCheck("gravamenes", "Gravámenes y limitaciones", Unknown, Provider, "Sin información de gravámenes");

        var sinGravamenes = !IsSi(vehiculo.Gravamenes);
        var sinPrendas = !IsSi(vehiculo.Prendas);

        if (sinGravamenes && sinPrendas)
            return new ConsultationCheck("gravamenes", "Gravámenes y limitaciones", Ok, Provider, null);

        return new ConsultationCheck("gravamenes", "Gravámenes y limitaciones", Warn, Provider, "El vehículo tiene gravámenes o prendas");
    }

    private static bool IsSi(string? value) =>
        string.Equals(value, "SI", StringComparison.OrdinalIgnoreCase);

    private static List<HydratedField> MapHydratedFields(KyverumRuntVehicleData? data)
    {
        var fields = new List<HydratedField>();

        // Tipo de documento del propietario resuelto por el RUNT (dato canónico): siembra
        // owner_document_type en código FLIT para el paso vendedor de traspaso (HU #10478). Va antes
        // del guard de vehiculo null porque vive al nivel de data, no del vehículo.
        Add(fields, "owner_document_type", MapOwnerDocType(data?.TipoDocPropietario));

        var v = data?.Vehiculo;
        if (v is null)
            return fields;

        Add(fields, "plate", v.Placa);
        Add(fields, "vin", v.Vin);
        Add(fields, "vehicle_year", v.Modelo);
        Add(fields, "vehicle_brand", v.Marca);
        Add(fields, "vehicle_line", v.Linea);
        Add(fields, "vehicle_color", v.Color);
        Add(fields, "vehicle_class", v.Clase);
        Add(fields, "vehicle_fuel", v.TipoCombustible);
        Add(fields, "vehicle_engine_displacement", v.Cilindraje);
        Add(fields, "transit_office_name", v.OrganismoTransito);
        Add(fields, "vehicle_state", v.EstadoAutomotor);
        Add(fields, "vehicle_service", v.TipoServicio);
        Add(fields, "vehicle_body_type", v.TipoCarroceria);
        Add(fields, "vehicle_chassis", v.NumChasis);
        Add(fields, "vehicle_engine_number", v.NumMotor);
        Add(fields, "vehicle_series", v.NumSerie);
        Add(fields, "vehicle_passengers", v.PasajerosSentados);
        Add(fields, "vehicle_weight", v.PesoBruto ?? data?.DatosTecnicos?.PesoBrutoVehicular);
        Add(fields, "vehicle_axles", v.NumeroEjes ?? data?.DatosTecnicos?.NoEjes);

        // SOAT: preferir vigente; si no, el primero disponible.
        var soat = data?.Soat?.FirstOrDefault(s =>
            string.Equals(s?.Estado, "VIGENTE", StringComparison.OrdinalIgnoreCase))
            ?? data?.Soat?.FirstOrDefault();
        Add(fields, "soat_vencimiento", soat?.FechaVencimSoat);
        Add(fields, "soat_aseguradora", soat?.RazonSocialAsegur);
        // HU #10856 — estado real del SOAT para el certificado.
        // HU #10973 — NORMALIZADO al vocabulario de SoatGate: esta llave alimenta también el gate de
        // aprobación del OT, y el frontend compara estricto contra "vigente" en minúscula
        // (lib/tramites/estados.ts). Antes se escribía el crudo del RUNT ("VIGENTE"), que bloqueaba
        // la aprobación en trámites con SOAT vigente.
        Add(fields, SoatGate.FieldKey, SoatGate.Normalize(soat?.Estado));

        // RTM: preferir la vigente ("SI"); si no, la primera.
        var rtm = data?.Rtm?.FirstOrDefault(t =>
            string.Equals(t?.Vigente, "SI", StringComparison.OrdinalIgnoreCase))
            ?? data?.Rtm?.FirstOrDefault();
        Add(fields, "rtm_vencimiento", rtm?.FechaVencimientoRvt);
        Add(fields, "rtm_estado", MapVigencia(rtm?.Vigente)); // HU #10856 — estado real de la RTM para el certificado.

        return fields;
    }

    private static void Add(List<HydratedField> fields, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            fields.Add(new HydratedField(key, value, null));
    }

    // Inverso de KyverumRuntDocType.Normalize: código RUNT del propietario → tipo de documento FLIT
    // (ActorDocumentType: CC/CE/NIT/PAS/TI). 'Y' u otros sin equivalente FLIT ⇒ null (no se siembra).
    private static string? MapOwnerDocType(string? tipoDocPropietario) =>
        tipoDocPropietario?.Trim().ToUpperInvariant() switch
        {
            "C" => "CC",
            "N" => "NIT",
            "E" => "CE",
            "T" => "TI",
            "P" => "PAS",
            _ => null,
        };

    // Normaliza el "vigente" del RUNT ("SI"/"NO"/"NO APLICA") al vocabulario de vigencia que ya usa
    // soat_estado ("VIGENTE"/"NO VIGENTE"), para que el certificado (HU #10856) muestre lo mismo sin
    // importar el proveedor.
    private static string? MapVigencia(string? vigente) =>
        vigente?.Trim().ToUpperInvariant() switch
        {
            "SI" => "VIGENTE",
            "NO" => "NO VIGENTE",
            "NO APLICA" => "NO APLICA",
            _ => null,
        };

    private static string ComputeOverall(IReadOnlyList<ConsultationCheck> checks)
    {
        if (checks.Any(c => c.Status == Fail))
            return Red;

        if (checks.Any(c => c.Status == Warn))
            return Yellow;

        if (checks.Any(c => c.Status == Ok))
            return Green;

        return Yellow;
    }
}
