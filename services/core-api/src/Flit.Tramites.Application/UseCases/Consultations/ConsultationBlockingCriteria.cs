namespace Flit.Tramites.Application.UseCases.Consultations;

/// <summary>
/// FEATURE 05 — criterios del preflight cuyo carácter bloqueante (rojo) vs. informativo (amarillo)
/// una compañía puede configurar por Organismo de Tránsito, vistos desde Trámites. Espejo deliberado
/// de <c>BlockingCriteria</c> (Flit.Admin.Domain): Application de Trámites no referencia Admin.Domain
/// — el cruce ocurre en Infraestructura, vía <c>ConsultationBlockingPolicy</c>.
///
/// ADVERTENCIA — colisión de vocabulario: NO es lo mismo que <see cref="ConsultationRestrictionKinds"/>
/// (rnmc|fines), que decide SI la consulta corre, ni que <see cref="ConsultationKind"/>
/// (vehicle_vin|vehicle_plate|conductor), que enumera el TIPO de dato consultado.
/// </summary>
public static class ConsultationBlockingCriteria
{
    public const string Soat = "soat";
    public const string Rtm = "rtm";
    public const string EstadoVehiculo = "estado_vehiculo";
    public const string Fines = "fines";
    public const string Rnmc = "rnmc";

    /// <summary>
    /// Default del criterio cuando la compañía NO configuró una fila para el par (tenant, OT).
    /// Preserva el comportamiento previo a esta feature: SOAT/RTM/estado bloqueaban (fail→rojo) y
    /// comparendos/RNMC solo advertían (warn→amarillo). Un criterio desconocido no bloquea.
    /// </summary>
    public static bool DefaultBlocks(string criterion) => criterion switch
    {
        Soat => true,
        Rtm => true,
        EstadoVehiculo => true,
        Fines => false,
        Rnmc => false,
        _ => false,
    };
}
