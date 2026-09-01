using Flit.Tramites.Application.UseCases.Certifications;
using Flit.Tramites.Domain.Certifications;
using Flit.Tramites.Domain.Tramites.Services;

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

    /// <summary>Versión del mapeo; se persiste con cada fila certificada (HU #11303, ADR-0041).</summary>
    public const string MapperVersion = "intempo-v2";

    private static readonly TimeSpan ColombiaOffset = TimeSpan.FromHours(-5);

    public static ConsultationResult Map(IntempoVehicleResponse response) =>
        Map(response, DateOnly.FromDateTime(DateTimeOffset.UtcNow.ToOffset(ColombiaOffset).Date));

    /// <summary>Sobrecarga con la fecha inyectada, para que las pruebas no dependan del reloj.</summary>
    public static ConsultationResult Map(IntempoVehicleResponse response, DateOnly today)
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
            MapGravamenes(response.TieneGravamenes, response.Prendas, response.LimitacionesPropiedad, response.Gravamenes),
        };

        var hydrated = MapHydratedFields(response);
        var overall = ComputeOverall(checks);
        var certifications = MapCertifications(response, today);

        return new ConsultationResult(Provider, overall, checks, hydrated, Certifications: certifications);
    }

    /// <summary>
    /// Traduce la respuesta al vocabulario canónico (HU #11303, ADR-0041). Intempo <b>no tiene bloque
    /// de revisión técnico-mecánica</b> en su contrato: el bundle sale sin RTM y no se declara una
    /// inventada. Sí aporta las seis celdas del SOAT y la fecha de matrícula.
    /// </summary>
    private static CertificationBundle? MapCertifications(IntempoVehicleResponse response, DateOnly today)
    {
        var soat = (response.SoatNacionales ?? [])
            .Where(s => s is not null)
            .Select(s => CertificationFactory.Soat(
                s.NoPoliza,
                s.EntidadExpideSoat,
                s.FechaExpedicion,
                s.FechaVigencia,
                s.FechaVencimiento,
                s.Estado));

        var vehicle = CertificationFactory.Vehicle(response.FechaMatricula);

        return CertificationFactory.VehicleBundle(soat, [], vehicle, today);
    }

    private static ConsultationCheck MapEstadoVehiculo(string? estado)
    {
        if (string.IsNullOrWhiteSpace(estado))
            return new ConsultationCheck("estado_vehiculo", "Estado del vehículo", Unknown, Provider, "Sin información de estado");

        var activo = string.Equals(estado, "ACTIVO", StringComparison.OrdinalIgnoreCase);
        var estadoDatos = ConsultationCheckDetail.Datos(("Estado", estado.Trim().ToUpperInvariant()));
        return new ConsultationCheck(
            "estado_vehiculo",
            "Estado del vehículo",
            activo ? Ok : Fail,
            Provider,
            // El mensaje repite los datos en una línea: respaldo si el campo estructurado se pierde
            // por el camino, y para los expedientes cuyo pre-vuelo se guardó antes de que existiera.
            ConsultationCheckDetail.Resumen(estadoDatos),
            Datos: estadoDatos);
    }

    private static ConsultationCheck MapSoat(List<IntempoSoat>? soat)
    {
        if (soat is null || soat.Count == 0)
            return new ConsultationCheck("soat", "SOAT", Fail, Provider, "Sin SOAT registrado");

        var poliza = soat.FirstOrDefault(s =>
            string.Equals(s.Estado, "VIGENTE", StringComparison.OrdinalIgnoreCase));
        var vigente = poliza is not null;
        var soatDatos = vigente
            ? ConsultationCheckDetail.Datos(
                ("Vigente hasta", ConsultationCheckDetail.Fecha(poliza?.FechaVencimiento)),
                ("Póliza", poliza?.NoPoliza))
            : null;
        return new ConsultationCheck(
            "soat",
            "SOAT",
            vigente ? Ok : Fail,
            Provider,
            vigente ? ConsultationCheckDetail.Resumen(soatDatos) : "SOAT vencido o no vigente",
            Datos: soatDatos);
    }

    private static ConsultationCheck MapGravamenes(
        string? tieneGravamenes,
        string? prendas,
        List<object>? limitaciones,
        List<IntempoGravamen>? gravamenesDetalle)
    {
        var sinGravamenes = IsNo(tieneGravamenes);
        var sinPrendas = IsNo(prendas);
        var sinLimitaciones = limitaciones is null || limitaciones.Count == 0;
        var sinDetalle = gravamenesDetalle is null || gravamenesDetalle.Count == 0;

        if (sinGravamenes && sinPrendas && sinLimitaciones && sinDetalle)
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
            $"El vehículo tiene gravámenes, prendas o limitaciones (gravámenes: {NormSiNo(tieneGravamenes)} · prendas: {NormSiNo(prendas)})");
    }

    private static string NormSiNo(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "—" : value.Trim().ToUpperInvariant();

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

        if (!string.IsNullOrWhiteSpace(r.Color))
            fields.Add(new HydratedField("vehicle_color", r.Color, null));

        if (!string.IsNullOrWhiteSpace(r.ClaseVehiculo))
            fields.Add(new HydratedField("vehicle_class", r.ClaseVehiculo, null));

        if (!string.IsNullOrWhiteSpace(r.TipoCombustible))
            fields.Add(new HydratedField("vehicle_fuel", r.TipoCombustible, null));

        if (!string.IsNullOrWhiteSpace(r.Cilindraje))
            fields.Add(new HydratedField("vehicle_engine_displacement", r.Cilindraje, null));

        if (!string.IsNullOrWhiteSpace(r.OrganismoTransito))
            fields.Add(new HydratedField("transit_office_name", r.OrganismoTransito, null));

        if (!string.IsNullOrWhiteSpace(r.EstadoDelVehiculo))
            fields.Add(new HydratedField("vehicle_state", r.EstadoDelVehiculo, null));

        // HU #11137 — paridad con los otros proveedores del RUNT. Estos tres campos ya venían
        // deserializados y se descartaban.
        if (!string.IsNullOrWhiteSpace(r.TipoServicio))
            fields.Add(new HydratedField("vehicle_service", r.TipoServicio, null));

        if (!string.IsNullOrWhiteSpace(r.TipoCarroceria))
            fields.Add(new HydratedField("vehicle_body_type", r.TipoCarroceria, null));

        if (!string.IsNullOrWhiteSpace(r.NoChasis))
            fields.Add(new HydratedField("vehicle_chassis", r.NoChasis, null));

        // Insumo de la regla de antigüedad de la RTM (HU #11136).
        if (!string.IsNullOrWhiteSpace(r.FechaMatricula))
            fields.Add(new HydratedField("vehicle_registration_date", r.FechaMatricula, null));

        // Señal RUNT de prenda/gravamen (+ detalle de acreedores cuando Intempo lo trae).
        Add(fields, "runt_tiene_gravamenes", r.TieneGravamenes);
        Add(fields, "runt_tiene_prendas", r.Prendas);
        Add(fields, "runt_prendario", r.Prendario);
        Add(fields, "runt_nombre_acreedor", r.NombreAcreedor);
        if (r.Gravamenes is { Count: > 0 } detalle)
        {
            fields.Add(new HydratedField(
                "runt_gravamenes",
                null,
                System.Text.Json.JsonSerializer.Serialize(detalle)));
        }

        // HU #11137 — SOAT. Este mapper producía una verificación de estado y NINGÚN campo, así que un
        // trámite consultado por Intempo emitía la tabla certificadora del SOAT entera en blanco. El
        // modelo ya declaraba los seis campos: solo faltaba persistirlos.
        // Misma selección que los demás proveedores: el vigente y, si no hay, el primero.
        var soat = r.SoatNacionales?.FirstOrDefault(s =>
            string.Equals(s?.Estado, "VIGENTE", StringComparison.OrdinalIgnoreCase))
            ?? r.SoatNacionales?.FirstOrDefault();

        Add(fields, "soat_poliza", soat?.NoPoliza);
        Add(fields, "soat_vigencia", soat?.FechaVigencia);
        Add(fields, "soat_expedicion", soat?.FechaExpedicion);
        Add(fields, "soat_vencimiento", soat?.FechaVencimiento);
        Add(fields, "soat_aseguradora", soat?.EntidadExpideSoat);
        // NORMALIZADO al vocabulario del gate: esta llave alimenta también la aprobación del OT y el
        // frontend la compara estricto contra "vigente" en minúscula. El crudo del RUNT la bloquearía.
        Add(fields, SoatGate.FieldKey, SoatGate.Normalize(soat?.Estado));

        // RTM: el contrato de Intempo NO tiene bloque de revisión técnico-mecánica. No se declara uno
        // inventado (ver VerifikTecnomecanica): con este proveedor la tabla de RTM depende del OCR del
        // PDF, y el modelo lo dice en vez de aparentar cobertura.
        return fields;
    }

    private static void Add(List<HydratedField> fields, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            fields.Add(new HydratedField(key, value, null));
    }

    private static string ComputeOverall(IReadOnlyList<ConsultationCheck> checks)
    {
        if (checks.Any(c => c.Status == Fail)) return Red;
        if (checks.Any(c => c.Status == Warn)) return Yellow;
        if (checks.Any(c => c.Status == Ok)) return Green;
        return Yellow;
    }
}
