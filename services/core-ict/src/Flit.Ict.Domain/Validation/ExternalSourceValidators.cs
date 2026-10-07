using System.Text.Json.Serialization;

namespace Flit.Ict.Domain.Validation;

/// <summary>Datos normalizados de las consultas a fuentes externas de un pre-trámite.</summary>
public sealed record ConsultationResult(
    string? SoatStatus = null,
    string? RtmStatus = null,
    int? VehicleModelYear = null,
    bool HasActiveSanctions = false,
    bool? PazYSalvo = null,
    string? TransitOfficeName = null,
    [property: JsonIgnore] VehicleConsultationSnapshot? Vehicle = null);

/// <summary>
/// Bug #13304 — resultado COMPLETO de la consulta de vehículo (VEHICLE/VIN) que core-api devuelve para que
/// el borrador lo reutilice sin volver a consultar el RUNT. <see cref="SnapshotJson"/> es OPACO para
/// core-ict (lo serializa y lo lee core-api) y contiene PII (titular, acreedor): no se loguea, no va en
/// <c>query_response</c> y se purga al materializar. <see cref="Plate"/>/<see cref="Vin"/> son el
/// identificador con el que se consultó (los pone el orquestador según <see cref="Kind"/>).
/// </summary>
public sealed record VehicleConsultationSnapshot(
    string SnapshotJson,
    DateTimeOffset ConsultedAt,
    string Provider,
    string Kind,
    string Plate = "",
    string Vin = "")
{
    /// <summary>Kind de la consulta por placa (mismo literal que <c>ConsultationKind</c> de core-api).</summary>
    public const string KindPlate = "VehiclePlate";

    /// <summary>Kind de la consulta por VIN.</summary>
    public const string KindVin = "VehicleVin";
}

/// <summary>
/// Validadores de negocio externo portados de v1 (SOAT, RTM/antigüedad, RNMC, paz y salvo).
/// Lógica pura (sin I/O) — el orquestador les pasa el resultado normalizado y ellos deciden si
/// el pre-trámite queda con novedades (bloqueantes) o solo con una advertencia informativa.
/// </summary>
public static class ExternalSourceValidators
{
    /// <summary>
    /// Validaciones BLOQUEANTES: si alguna falla, el pre-trámite queda con novedades (ps=4) y NO pasa a
    /// borrador. Vehículo existente en RUNT (solo traspaso), SOAT (vigente), RTM (según antigüedad; traspaso
    /// unilateral tipo 4 solo advierte), RNMC (sanciones/medidas correctivas activas) y, desde el Bug #13109
    /// (punto 9), el paz y salvo del conductor (DRIVER) cuando la fuente responde explícitamente que no.
    /// Sin dato de paz y salvo (null) no se bloquea.
    /// </summary>
    /// <param name="queryType">
    /// Tipo de la consulta que produjo <paramref name="result"/> ('VEHICLE'/'VIN'/'RNMC'/'DRIVER'). El
    /// orquestador valida CADA source_query por separado; se usa para que la regla de "vehículo no
    /// encontrado" solo mire la consulta de vehículo y no las de conductor/RNMC (que traen vehículo en null).
    /// </param>
    public static IReadOnlyList<string> Validate(int transactionType, string queryType, ConsultationResult result, int currentYear)
    {
        ArgumentNullException.ThrowIfNull(result);
        var issues = new List<string>();

        // Traspaso (3,4): el vehículo DEBE existir en el RUNT. La consulta por placa (query_type 'VEHICLE',
        // que el SP externo genera SOLO para 3/4) que no devuelve NINGÚN dato del vehículo (ni SOAT ni
        // año-modelo) = vehículo no encontrado → novedad. Sin esta regla, con fuentes reales una placa
        // inexistente materializaba igual (todo null no bloqueaba). En matrícula la consulta es 'VIN' y el
        // vehículo aún no está en el RUNT, así que un resultado vacío ahí es válido (no entra aquí).
        if (transactionType is 3 or 4
            && string.Equals(queryType, "VEHICLE", StringComparison.OrdinalIgnoreCase)
            && result.SoatStatus is null
            && result.VehicleModelYear is null)
        {
            issues.Add("No se pudo verificar el vehículo en el RUNT (el traspaso requiere un vehículo registrado)");
        }

        // SOAT: obligatorio y vigente.
        if (result.SoatStatus is not null
            && !string.Equals(result.SoatStatus, "VIGENTE", StringComparison.OrdinalIgnoreCase))
        {
            issues.Add("SOAT no vigente");
        }

        // RTM: requerida según antigüedad (>5 años). Traspaso unilateral (4) solo advierte (no bloquea).
        if (result.VehicleModelYear is { } modelYear)
        {
            var age = currentYear - modelYear;
            var rtmRequired = age > 5;
            var rtmVigente = string.Equals(result.RtmStatus, "VIGENTE", StringComparison.OrdinalIgnoreCase);
            if (rtmRequired && !rtmVigente && transactionType != 4)
            {
                issues.Add("Revisión técnico-mecánica (RTM) no vigente");
            }
        }

        // RNMC: bloquea si hay sanciones/medidas correctivas activas.
        if (result.HasActiveSanctions)
        {
            issues.Add("El actor tiene sanciones o medidas correctivas activas (RNMC)");
        }

        // Paz y salvo (Bug #13109, punto 9): regla de producto de FLIT 2.0, distinta de v1 (donde era
        // informativo). Solo la consulta de conductor (DRIVER) y solo con un «no» explícito: sin dato no se
        // bloquea, y las demás consultas (p. ej. RUES) siguen como estaban, en Warnings. El orquestador marca
        // la novedad (ps=4) y SendToCoreApiJob no materializa el borrador.
        if (string.Equals(queryType, "DRIVER", StringComparison.OrdinalIgnoreCase) && result.PazYSalvo == false)
        {
            issues.Add("Actor sin paz y salvo: presenta multas pendientes con los organismos de tránsito");
        }

        return issues;
    }

    /// <summary>
    /// Advertencias INFORMATIVAS: se registran para que el gestor/OT las vean, pero NO bloquean el paso a
    /// borrador. El orquestador solo las registra cuando <see cref="Validate"/> no encontró novedades, así que
    /// para la consulta DRIVER (que desde el Bug #13109 bloquea en <see cref="Validate"/>) esta advertencia
    /// ya no se escribe; queda para el paz y salvo que llegue por otras consultas.
    /// </summary>
    public static IReadOnlyList<string> Warnings(ConsultationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        var warnings = new List<string>();

        // DRIVER (paz y salvo del conductor): v1 (validateDriverRequest, paceAndSafe='NO') registra la
        // novedad informativa y retorna OK; el trámite NO se bloquea.
        if (result.PazYSalvo == false)
        {
            warnings.Add("El actor presenta multas pendientes; el organismo de tránsito no aprobará la solicitud hasta resolverlas (paz y salvo)");
        }

        return warnings;
    }
}
