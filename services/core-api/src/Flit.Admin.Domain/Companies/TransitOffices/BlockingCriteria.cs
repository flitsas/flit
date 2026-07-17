namespace Flit.Admin.Domain.Companies.TransitOffices;

/// <summary>
/// Vocabulario cerrado de criterios del preflight que una compañía puede marcar como
/// bloqueantes o meramente informativos para un Organismo de Tránsito puntual (FEATURE 05 —
/// bloqueo de preflight configurable). Refleja el CHECK de
/// <c>admin.tenant_transit_office_blocking_policies.criterion</c>
/// (34-F05-ot-blocking-policies.sql).
///
/// ADVERTENCIA — colisión de vocabulario: NO es lo mismo que
/// <c>RestrictedConsultationKinds</c> (rnmc|fines), que decide SI la consulta se ejecuta.
/// Este vocabulario decide, para una consulta que SÍ corre, si un hallazgo negativo BLOQUEA
/// (rojo, subsanable) o solo ADVIERTE (amarillo). Tampoco es <c>ConsultationKind</c>
/// (vehicle_vin|vehicle_plate|conductor), que enumera el TIPO de dato consultado.
///
/// Su espejo en Trámites es
/// <c>Flit.Tramites.Application.UseCases.Consultations.ConsultationBlockingCriteria</c>;
/// Infraestructura traduce entre ambos. Prohibido importar el vocabulario de Trámites aquí.
/// </summary>
public static class BlockingCriteria
{
    /// <summary>SOAT vencido o no vigente.</summary>
    public const string Soat = "soat";

    /// <summary>Revisión técnico-mecánica (RTM) no vigente.</summary>
    public const string Rtm = "rtm";

    /// <summary>Estado del vehículo en RUNT distinto de "ACTIVO" (incluye no inscrito).</summary>
    public const string EstadoVehiculo = "estado_vehiculo";

    /// <summary>Comparendos (multas SIMIT / acuerdos de pago) pendientes.</summary>
    public const string Fines = "fines";

    /// <summary>Medidas correctivas RNMC (Policía).</summary>
    public const string Rnmc = "rnmc";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        Soat,
        Rtm,
        EstadoVehiculo,
        Fines,
        Rnmc,
    };

    public static bool IsValid(string? criterion) =>
        !string.IsNullOrWhiteSpace(criterion) && All.Contains(criterion);
}
