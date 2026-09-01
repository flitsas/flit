using System.Text.Json;
using Flit.Tramites.Application.UseCases.Certifications;
using Flit.Tramites.Domain.Certifications;
using Flit.Tramites.Domain.Tramites.Services;

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

    /// <summary>Versión del mapeo; se persiste con cada fila certificada (HU #11303, ADR-0041).</summary>
    public const string MapperVersion = "verifik-v2";

    private static readonly TimeSpan ColombiaOffset = TimeSpan.FromHours(-5);

    public static ConsultationResult MapVehicle(VerifikVehicleResponse response) =>
        MapVehicle(response, DateOnly.FromDateTime(DateTimeOffset.UtcNow.ToOffset(ColombiaOffset).Date));

    /// <summary>Sobrecarga con la fecha inyectada, para que las pruebas no dependan del reloj.</summary>
    public static ConsultationResult MapVehicle(VerifikVehicleResponse response, DateOnly today)
    {
        var info = response.Data?.InformacionGeneral;

        var checks = new List<ConsultationCheck>
        {
            MapEstadoVehiculo(info),
            MapSoat(response.Data?.Soat),
            MapTecnomecanica(response.Data?.TecnoMecanica),
            MapGravamenes(info),
        };

        var hydrated = MapHydratedFields(response.Data);
        var overall = ComputeOverall(checks);
        var certifications = MapCertifications(response.Data, today);

        return new ConsultationResult(Provider, overall, checks, hydrated, Certifications: certifications);
    }

    /// <summary>
    /// Traduce la respuesta al vocabulario canónico (HU #11303, ADR-0041), con el histórico completo
    /// de pólizas y revisiones.
    /// </summary>
    private static CertificationBundle? MapCertifications(VerifikVehicleData? data, DateOnly today)
    {
        var soat = (data?.Soat ?? [])
            .Where(s => s is not null)
            .Select(s => CertificationFactory.Soat(
                s.NoPoliza,
                s.EntidadExpideSoat,
                s.FechaExpedicion,
                s.FechaVigencia,
                s.FechaVencimiento,
                s.Estado));

        // La vigencia sale de `vigente`, no de `estado`: en la RTM ese campo describe el resultado del
        // trámite de la revisión ("APROBADA") y no su cobertura.
        var rtm = (data?.TecnoMecanica ?? [])
            .Where(t => t is not null)
            .Select(t => CertificationFactory.Rtm(
                certificateNumber: null,
                cda: t.CdaExpide,
                issuedOn: null,
                validFrom: null,
                validUntil: t.FechaVencimiento,
                status: t.Vigente ?? t.Estado));

        var vehicle = CertificationFactory.Vehicle(data?.InformacionGeneral?.FechaMatricula);

        return CertificationFactory.VehicleBundle(soat, rtm, vehicle, today);
    }

    private static ConsultationCheck MapEstadoVehiculo(VerifikInformacionGeneral? info)
    {
        var estado = info?.EstadoDelVehiculo;
        if (string.IsNullOrWhiteSpace(estado))
            return new ConsultationCheck("estado_vehiculo", "Estado del vehículo", Unknown, Provider, "Sin información de estado");

        var isActivo = string.Equals(estado, "ACTIVO", StringComparison.OrdinalIgnoreCase);
        var estadoDatos = ConsultationCheckDetail.Datos(("Estado", estado.Trim().ToUpperInvariant()));
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

    private static ConsultationCheck MapSoat(List<VerifikSoat>? soat)
    {
        // RUNT real: soat es un array. Vacío → unknown (sin información),
        // algún item VIGENTE → ok, todos vencidos/otros → fail.
        if (soat is null || soat.Count == 0)
            return new ConsultationCheck("soat", "SOAT", Unknown, Provider, "Sin SOAT registrado");

        var poliza = soat.FirstOrDefault(s =>
            string.Equals(s?.Estado, "VIGENTE", StringComparison.OrdinalIgnoreCase));
        var vigente = poliza is not null;
        var soatDatos = vigente
            ? ConsultationCheckDetail.Datos(
                ("Vigente hasta", ConsultationCheckDetail.Fecha(poliza?.FechaVencimiento)),
                ("Póliza", poliza?.NoPoliza),
                ("Aseguradora", poliza?.EntidadExpideSoat))
            : null;
        return new ConsultationCheck(
            "soat",
            "SOAT",
            vigente ? Ok : Fail,
            Provider,
            vigente ? ConsultationCheckDetail.Resumen(soatDatos) : "SOAT vencido o no vigente",
            Datos: soatDatos);
    }

    private static ConsultationCheck MapTecnomecanica(List<VerifikTecnomecanica>? tecno)
    {
        // RUNT real: tecnoMecanica es un array (puede venir vacío). Tomamos la revisión
        // vigente="SI" si existe; si no, evaluamos la forma del resto.
        if (tecno is null || tecno.Count == 0)
            return new ConsultationCheck("tecnomecanica", "Revisión técnico-mecánica", Unknown, Provider, "Sin información de tecnomecánica");

        var revision = tecno.FirstOrDefault(t =>
            string.Equals(t?.Vigente, "SI", StringComparison.OrdinalIgnoreCase));
        if (revision is not null)
        {
            var rtmDatos = ConsultationCheckDetail.Datos(
                ("Vigente hasta", ConsultationCheckDetail.Fecha(revision.FechaVencimiento)),
                ("CDA", revision.CdaExpide));
            return new ConsultationCheck(
                "tecnomecanica",
                "Revisión técnico-mecánica",
                Ok,
                Provider,
                ConsultationCheckDetail.Resumen(rtmDatos),
                Datos: rtmDatos);
        }

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
            $"El vehículo tiene gravámenes o prendas (gravámenes: {NormSiNo(info.TieneGravamenes)} · prendas: {NormSiNo(info.Prendas)})");
    }

    private static string NormSiNo(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "—" : value.Trim().ToUpperInvariant();

    private static bool IsSi(string? value) =>
        string.Equals(value, "SI", StringComparison.OrdinalIgnoreCase);

    private static List<HydratedField> MapHydratedFields(VerifikVehicleData? data)
    {
        var info = data?.InformacionGeneral;
        if (info is null)
            return [];

        var fields = new List<HydratedField>();

        if (!string.IsNullOrWhiteSpace(info.NoPlaca))
            fields.Add(new HydratedField("plate", info.NoPlaca, null));

        if (!string.IsNullOrWhiteSpace(info.NoVin))
            fields.Add(new HydratedField("vin", info.NoVin, null));

        if (!string.IsNullOrWhiteSpace(info.Modelo))
            fields.Add(new HydratedField("vehicle_year", info.Modelo, null));

        if (!string.IsNullOrWhiteSpace(info.Marca))
            fields.Add(new HydratedField("vehicle_brand", info.Marca, null));

        if (!string.IsNullOrWhiteSpace(info.Linea))
            fields.Add(new HydratedField("vehicle_line", info.Linea, null));

        if (!string.IsNullOrWhiteSpace(info.Color))
            fields.Add(new HydratedField("vehicle_color", info.Color, null));

        if (!string.IsNullOrWhiteSpace(info.ClaseVehiculo))
            fields.Add(new HydratedField("vehicle_class", info.ClaseVehiculo, null));

        if (!string.IsNullOrWhiteSpace(info.TipoCombustible))
            fields.Add(new HydratedField("vehicle_fuel", info.TipoCombustible, null));

        if (!string.IsNullOrWhiteSpace(info.CilindrajeNormalizado))
            fields.Add(new HydratedField("vehicle_engine_displacement", info.CilindrajeNormalizado, null));

        if (!string.IsNullOrWhiteSpace(info.OrganismoTransito))
            fields.Add(new HydratedField("transit_office_name", info.OrganismoTransito, null));

        if (!string.IsNullOrWhiteSpace(info.EstadoDelVehiculo))
            fields.Add(new HydratedField("vehicle_state", info.EstadoDelVehiculo, null));

        if (!string.IsNullOrWhiteSpace(info.TipoServicio))
            fields.Add(new HydratedField("vehicle_service", info.TipoServicio, null));

        if (!string.IsNullOrWhiteSpace(info.TipoCarroceria))
            fields.Add(new HydratedField("vehicle_body_type", info.TipoCarroceria, null));

        if (!string.IsNullOrWhiteSpace(info.NoChasis))
            fields.Add(new HydratedField("vehicle_chassis", info.NoChasis, null));

        if (!string.IsNullOrWhiteSpace(info.NoMotor))
            fields.Add(new HydratedField("vehicle_engine_number", info.NoMotor, null));

        if (!string.IsNullOrWhiteSpace(info.NoSerie))
            fields.Add(new HydratedField("vehicle_series", info.NoSerie, null));

        if (!string.IsNullOrWhiteSpace(info.PasajerosSentados))
            fields.Add(new HydratedField("vehicle_passengers", info.PasajerosSentados, null));

        if (!string.IsNullOrWhiteSpace(info.PesoBruto))
            fields.Add(new HydratedField("vehicle_weight", info.PesoBruto, null));

        if (!string.IsNullOrWhiteSpace(info.NoEjes))
            fields.Add(new HydratedField("vehicle_axles", info.NoEjes, null));

        if (!string.IsNullOrWhiteSpace(info.FechaMatricula))
            fields.Add(new HydratedField("vehicle_registration_date", info.FechaMatricula, null));

        // Señal RUNT de prenda/gravamen para el paso Prenda (desplegable junto a la alerta).
        AddSiHay(fields, "runt_tiene_gravamenes", info.TieneGravamenes);
        AddSiHay(fields, "runt_tiene_prendas", info.Prendas);
        if (data?.GarantiasMobiliarias is { Count: > 0 } garantias)
        {
            fields.Add(new HydratedField(
                "runt_gravamenes",
                null,
                JsonSerializer.Serialize(garantias)));
        }

        // SOAT: tomar el vigente; si no, el primero disponible.
        var soat = data?.Soat?.FirstOrDefault(s =>
            string.Equals(s?.Estado, "VIGENTE", StringComparison.OrdinalIgnoreCase))
            ?? data?.Soat?.FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(soat?.FechaVencimiento))
            fields.Add(new HydratedField("soat_vencimiento", soat.FechaVencimiento, null));
        if (!string.IsNullOrWhiteSpace(soat?.EntidadExpideSoat))
            fields.Add(new HydratedField("soat_aseguradora", soat.EntidadExpideSoat, null));

        // HU #11134 — póliza y fechas del SOAT desde el RUNT. Hasta ahora estas tres celdas del
        // certificado solo se llenaban con el OCR del PDF cargado, así que un trámite sin ese
        // documento (o con un OCR fallido) emitía el certificado a medias. El OCR se conserva como
        // respaldo: su handler ya respeta la precedencia y nunca pisa un valor de consulta.
        if (!string.IsNullOrWhiteSpace(soat?.NoPoliza))
            fields.Add(new HydratedField("soat_poliza", soat.NoPoliza, null));
        if (!string.IsNullOrWhiteSpace(soat?.FechaVigencia))
            fields.Add(new HydratedField("soat_vigencia", soat.FechaVigencia, null));
        if (!string.IsNullOrWhiteSpace(soat?.FechaExpedicion))
            fields.Add(new HydratedField("soat_expedicion", soat.FechaExpedicion, null));

        // HU #10973 — el estado del SOAT alimenta el certificado de vigencia SOAT/RTM Y el gate de
        // aprobación del OT (SoatGate, HU #10804). Se persiste NORMALIZADO al vocabulario del gate:
        // Verifik devuelve "VIGENTE" en mayúscula y el frontend compara estricto contra "vigente"
        // (lib/tramites/estados.ts), así que el valor crudo bloquearía la aprobación del OT.
        var soatEstado = SoatGate.Normalize(soat?.Estado);
        if (soatEstado is not null)
            fields.Add(new HydratedField(SoatGate.FieldKey, soatEstado, null));

        // RTM: tomar la vigente; si no, la primera.
        var rtm = data?.TecnoMecanica?.FirstOrDefault(t =>
            string.Equals(t?.Vigente, "SI", StringComparison.OrdinalIgnoreCase))
            ?? data?.TecnoMecanica?.FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(rtm?.FechaVencimiento))
            fields.Add(new HydratedField("rtm_vencimiento", rtm.FechaVencimiento, null));

        // HU #10973 — estado y CDA que expide la RTM: ya venían deserializados en la respuesta y se
        // descartaban, dejando en blanco dos celdas del certificado. A diferencia del SOAT, no
        // alimentan ningún gate, así que se persisten tal cual los reporta el RUNT.
        if (!string.IsNullOrWhiteSpace(rtm?.Estado))
            fields.Add(new HydratedField("rtm_estado", rtm.Estado, null));
        if (!string.IsNullOrWhiteSpace(rtm?.CdaExpide))
            fields.Add(new HydratedField("rtm_entidad", rtm.CdaExpide, null));

        // HU #11303 — se RETIRA la búsqueda "tolerante" por nombres candidatos que introdujo la
        // HU #11135 para el número y las fechas de la RTM. No es una decisión de estilo: la medición
        // en base de datos muestra CERO filas de `rtm_numero` y `rtm_expedicion` en todo el ambiente,
        // así que en la práctica nunca acertó un nombre. Lo que sí hacía era dar cobertura aparente a
        // un hueco real, que es exactamente el mecanismo que originó este Feature.
        //
        // Cuando haya una captura real de Verifik con sección RTM (hoy imposible: el token está
        // vencido), se declaran los campos en VerifikTecnomecanica con su nombre verdadero. Mientras
        // tanto la celda queda en blanco —regla HU #10856— y el payload crudo guardado permite
        // verificar qué manda el proveedor sin una sonda manual.

        return fields;
    }

    private static void AddSiHay(List<HydratedField> fields, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            fields.Add(new HydratedField(key, value, null));
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
