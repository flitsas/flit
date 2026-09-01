using System.Text.Json;
using Flit.Tramites.Application.UseCases.Certifications;
using Flit.Tramites.Domain.Certifications;
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

    private static readonly JsonSerializerOptions GravamenJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    private const string Ok = "ok";
    private const string Warn = "warn";
    private const string Fail = "fail";
    private const string Unknown = "unknown";

    private const string Green = "green";
    private const string Yellow = "yellow";
    private const string Red = "red";

    /// <summary>
    /// Versión del mapeo. Se persiste con cada fila certificada: cuando se corrige un mapper, es lo
    /// que permite saber qué filas produjo el anterior y reprocesarlas desde el payload crudo sin
    /// volver a pagar la consulta. <c>v2</c> = HU #11303, la que empieza a leer póliza, fechas de
    /// expedición, número de certificado, CDA y fecha de matrícula.
    /// </summary>
    public const string MapperVersion = "kyverum-v2";

    public static ConsultationResult MapVehicle(KyverumRuntVehicleResponse response) =>
        MapVehicle(response, DateOnly.FromDateTime(DateTimeOffset.UtcNow.ToOffset(ColombiaOffset).Date));

    /// <summary>Sobrecarga con la fecha inyectada, para que las pruebas no dependan del reloj.</summary>
    public static ConsultationResult MapVehicle(KyverumRuntVehicleResponse response, DateOnly today)
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
        var certifications = MapCertifications(response.Data, today);

        return new ConsultationResult(Provider, overall, checks, hydrated, Certifications: certifications);
    }

    private static readonly TimeSpan ColombiaOffset = TimeSpan.FromHours(-5);

    /// <summary>
    /// Traduce la respuesta al vocabulario canónico (HU #11303, ADR-0041). Se conserva el
    /// <b>histórico completo</b> de pólizas y revisiones: ya vino en la misma respuesta pagada, y es
    /// lo que permite cambiar el criterio de selección más adelante sin volver a consultar.
    /// </summary>
    private static CertificationBundle? MapCertifications(KyverumRuntVehicleData? data, DateOnly today)
    {
        var soat = (data?.Soat ?? [])
            .Where(s => s is not null)
            .Select(s => CertificationFactory.Soat(
                s.NumSoat,
                s.RazonSocialAsegur,
                s.FechaExpediSoat ?? s.FechaExpedicion,
                s.FechaInicioPoliza,
                s.FechaVencimSoat,
                s.Estado));

        // OJO: la vigencia sale de `vigente`, NO de `estadoRvt`. Un "APROBADA" describe el resultado
        // del trámite de la revisión; hay vehículos con cuatro APROBADA y ninguna vigente.
        var rtm = (data?.Rtm ?? [])
            .Where(r => r is not null)
            .Select(r => CertificationFactory.Rtm(
                r.NumeCerti,
                r.NombreCda,
                r.FechaExpedicionRvt,
                // El RUNT no manda inicio de vigencia de la RTM; sí el vencimiento.
                validFrom: null,
                validUntil: r.FechaVencimientoRvt,
                status: r.Vigente,
                inspectionType: r.TipoRevision));

        var vehicle = CertificationFactory.Vehicle(
            data?.Vehiculo?.FechaRegistro ?? data?.Vehiculo?.FechaMatricula);

        return CertificationFactory.VehicleBundle(soat, rtm, vehicle, today);
    }

    private static ConsultationCheck MapEstadoVehiculo(KyverumRuntVehiculo? vehiculo)
    {
        var estado = vehiculo?.EstadoAutomotor;
        if (string.IsNullOrWhiteSpace(estado))
            return new ConsultationCheck("estado_vehiculo", "Estado del vehículo", Unknown, Provider, "Sin información de estado");

        var isActivo = string.Equals(estado, "ACTIVO", StringComparison.OrdinalIgnoreCase);
        var estadoDatos = ConsultationCheckDetail.Datos(("Estado", estado.Trim().ToUpperInvariant()));
        // También en OK se dice lo que el RUNT respondió: la tarjeta quedaba con la pastilla verde y
        // el cuerpo vacío, sin decir de dónde salía ese verde.
        return new ConsultationCheck(
            "estado_vehiculo",
            "Estado del vehículo",
            isActivo ? Ok : Fail,
            Provider,
            // El mensaje repite los datos en una línea: respaldo si el campo estructurado se pierde
            // por el camino, y para los expedientes cuyo pre-vuelo se guardó antes de que existiera.
            ConsultationCheckDetail.Resumen(estadoDatos),
            Datos: estadoDatos);
    }

    private static ConsultationCheck MapSoat(List<KyverumRuntSoat>? soat)
    {
        // Array de pólizas. Vacío → unknown; alguna VIGENTE → ok; todas no vigentes → fail.
        if (soat is null || soat.Count == 0)
            return new ConsultationCheck("soat", "SOAT", Unknown, Provider, "Sin SOAT registrado");

        var poliza = soat.FirstOrDefault(s =>
            string.Equals(s?.Estado, "VIGENTE", StringComparison.OrdinalIgnoreCase));
        var vigente = poliza is not null;
        var soatDatos = vigente
            ? ConsultationCheckDetail.Datos(
                ("Vigente hasta", ConsultationCheckDetail.Fecha(poliza?.FechaVencimSoat)),
                ("Póliza", poliza?.NumSoat),
                ("Aseguradora", poliza?.RazonSocialAsegur))
            : null;
        // El detalle de la póliza vigente: es lo que el gestor puede contrastar con el certificado.
        return new ConsultationCheck(
            "soat",
            "SOAT",
            vigente ? Ok : Fail,
            Provider,
            vigente ? ConsultationCheckDetail.Resumen(soatDatos) : "SOAT vencido o no vigente",
            Datos: soatDatos);
    }

    private static ConsultationCheck MapTecnomecanica(List<KyverumRuntRtm>? rtm)
    {
        // Array de revisiones (histórico). Mismo criterio que Verifik sobre el campo "vigente":
        // vacío/ausente → unknown (muchos vehículos nuevos no tienen RTM aún); alguna "SI" → ok;
        // todas "NO APLICA" → unknown; alguna "NO" → fail (vencida); resto → unknown.
        if (rtm is null || rtm.Count == 0)
            return new ConsultationCheck("tecnomecanica", "Revisión técnico-mecánica", Unknown, Provider, "Sin información de tecnomecánica");

        var revision = rtm.FirstOrDefault(t =>
            string.Equals(t?.Vigente, "SI", StringComparison.OrdinalIgnoreCase));
        if (revision is not null)
        {
            var rtmDatos = ConsultationCheckDetail.Datos(
                ("Vigente hasta", ConsultationCheckDetail.Fecha(revision.FechaVencimientoRvt)),
                ("Certificado", revision.NumeCerti),
                ("CDA", revision.NombreCda));
            return new ConsultationCheck(
                "tecnomecanica",
                "Revisión técnico-mecánica",
                Ok,
                Provider,
                ConsultationCheckDetail.Resumen(rtmDatos),
                Datos: rtmDatos);
        }

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
        {
            return new ConsultationCheck(
                "gravamenes", "Gravámenes y limitaciones", Ok, Provider,
                "Sin gravámenes ni prendas registradas en el RUNT");
        }

        return new ConsultationCheck(
            "gravamenes",
            "Gravámenes y limitaciones",
            Warn,
            Provider,
            $"El vehículo tiene gravámenes o prendas (gravámenes: {NormSiNo(vehiculo.Gravamenes)} · prendas: {NormSiNo(vehiculo.Prendas)})");
    }

    private static string NormSiNo(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "—" : value.Trim().ToUpperInvariant();

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

        // Señal RUNT de prenda/gravamen para el paso Prenda (desplegable junto a la alerta).
        Add(fields, "runt_tiene_gravamenes", v.Gravamenes);
        Add(fields, "runt_tiene_prendas", v.Prendas);

        // Detalle de acreedores: Kyverum lo trae en data.garantias (+ garantiasPrendas). Sin esto
        // el wizard solo veía SI/NO aunque el RUNT ya devolvía Bancolombia, NIT y fecha.
        var detallePrenda = NormalizeGarantias(data?.Garantias, data?.GarantiasPrendas);
        if (detallePrenda.Count > 0)
        {
            var primerAcreedor = detallePrenda
                .Select(d => d.NombreAcreedor)
                .FirstOrDefault(n => !string.IsNullOrWhiteSpace(n));
            Add(fields, "runt_nombre_acreedor", primerAcreedor);
            fields.Add(new HydratedField(
                "runt_gravamenes",
                null,
                JsonSerializer.Serialize(detallePrenda, GravamenJsonOptions)));
        }

        // Fecha de matrícula (HU #11303): Kyverum la manda en `fechaRegistro`; `fechaMatricula` llega
        // null en las tres capturas. Sin esta llave, la regla de antigüedad de la RTM no puede
        // evaluarse y el bloque de revisión del certificado queda permanentemente en "no aplica".
        Add(fields, "vehicle_registration_date", v.FechaRegistro ?? v.FechaMatricula);

        // SOAT: preferir vigente; si no, el primero disponible.
        var soat = data?.Soat?.FirstOrDefault(s =>
            string.Equals(s?.Estado, "VIGENTE", StringComparison.OrdinalIgnoreCase))
            ?? data?.Soat?.FirstOrDefault();
        Add(fields, "soat_vencimiento", soat?.FechaVencimSoat);
        Add(fields, "soat_aseguradora", soat?.RazonSocialAsegur);

        // HU #11303 — póliza y fechas del SOAT. El proveedor las manda en las tres consultas reales;
        // el DTO no las modelaba, así que estas celdas del certificado dependían del OCR del PDF y
        // `soat_expedicion` no tenía UNA SOLA fila en todo el ambiente.
        Add(fields, "soat_poliza", soat?.NumSoat);
        Add(fields, "soat_expedicion", soat?.FechaExpediSoat ?? soat?.FechaExpedicion);
        Add(fields, "soat_vigencia", soat?.FechaInicioPoliza);
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

        // HU #11303 — número, expedición y CDA de la revisión. Mismo caso que el SOAT: el proveedor
        // los manda, el DTO no los leía, y las cuatro llaves quedaban en cero filas. `nombreCda` llega
        // con espacio inicial en capturas reales, de ahí el Trim.
        Add(fields, "rtm_numero", rtm?.NumeCerti);
        Add(fields, "rtm_expedicion", rtm?.FechaExpedicionRvt);
        Add(fields, "rtm_entidad", rtm?.NombreCda?.Trim());

        return fields;
    }

    private static void Add(List<HydratedField> fields, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            fields.Add(new HydratedField(key, value, null));
    }

    /// <summary>
    /// Une <c>garantias</c> + <c>garantiasPrendas</c> y normaliza al shape Intempo
    /// (<c>nombreAcreedor</c>, etc.) para que el frontend parseé un solo contrato.
    /// </summary>
    private static List<NormalizedRuntGravamen> NormalizeGarantias(
        List<KyverumRuntGarantia>? garantias,
        List<KyverumRuntGarantia>? garantiasPrendas)
    {
        var result = new List<NormalizedRuntGravamen>();
        foreach (var g in (garantias ?? []).Concat(garantiasPrendas ?? []))
        {
            if (g is null) continue;
            var nombre = FirstNonEmpty(g.Acreedor, g.NombreAcreedor);
            if (string.IsNullOrWhiteSpace(nombre)
                && string.IsNullOrWhiteSpace(g.NumeroDocumentoAcreedor)
                && g.IdPrenda is null
                && string.IsNullOrWhiteSpace(g.FechaInscripcion))
            {
                continue;
            }

            result.Add(new NormalizedRuntGravamen(
                g.IdPrenda,
                g.TipoDocumentoAcreedor,
                g.NumeroDocumentoAcreedor,
                nombre,
                g.FechaInscripcion,
                g.EstadoPrenda));
        }

        return result;
    }

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim();

    private sealed record NormalizedRuntGravamen(
        long? IdPrenda,
        string? TipoDocumentoAcreedor,
        string? NumeroDocumentoAcreedor,
        string? NombreAcreedor,
        string? FechaInscripcion,
        string? EstadoPrenda);

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
