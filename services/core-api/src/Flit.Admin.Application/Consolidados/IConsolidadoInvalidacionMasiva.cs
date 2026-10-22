namespace Flit.Admin.Application.Consolidados;

/// <summary>
/// HU #12789 (Épica #12760) — invalida en bloque los consolidados persistidos (wizard y maestro) de
/// los trámites afectados por un cambio de configuración que vive FUERA del expediente: la prelación
/// documental del OT (ADR-0038), las escrituras y representantes legales de la compañía, y las firmas
/// de su baúl.
/// <para>
/// <c>ConsolidadoVigenciaTracker</c> solo observa las filas hijas del propio trámite; estos cambios
/// alteran el PDF sin tocar ninguna, así que sin este puerto el siguiente acceso servía el consolidado
/// cacheado. Aquí se bajan las banderas <c>consolidado_wizard_vigente</c> y
/// <c>consolidado_maestro_vigente</c>; el siguiente acceso lo reconstruye.
/// </para>
/// <para>
/// <b>Contrato</b>: cada método es UNA sola operación set-based (un <c>UPDATE</c>, sin cargar
/// entidades) y solo alcanza trámites en estado NO final: los aprobados, anulados y revocados
/// conservan el expediente con el que se decidieron. Devuelve el número de trámites invalidados.
/// </para>
/// Uso de ejemplo:
/// <code>await invalidacion.InvalidarPorCompaniaAsync(tenantId, ct);</code>
/// </summary>
public interface IConsolidadoInvalidacionMasiva
{
    /// <summary>
    /// AC1 — el OT cuyo tenant es <paramref name="otTenantId"/> reordenó la prelación del tipo de
    /// trámite <paramref name="procedureTypeId"/>: invalida los consolidados de los trámites radicados
    /// ante ese OT (<c>transit_office_id</c> resuelto por <c>admin.transit_office_profiles</c>) de ese
    /// tipo. Los trámites sin OT asignado (wizard) no usan la prelación y quedan fuera.
    /// </summary>
    Task<int> InvalidarPorPrelacionOtAsync(
        Guid otTenantId,
        Guid procedureTypeId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// AC2 — cambió una escritura o un representante legal del directorio del tenant
    /// <paramref name="tenantId"/>: invalida los consolidados de los trámites de esa compañía.
    /// </summary>
    Task<int> InvalidarPorCompaniaAsync(Guid tenantId, CancellationToken cancellationToken = default);

    /// <summary>
    /// AC3 — una firma del baúl del tenant <paramref name="tenantId"/> se dio de alta, se editó o se
    /// revocó. No existe relación persistida firma↔trámite (la firma se resuelve en cada generación
    /// por tenant + documento del firmante, <c>ISignatureVaultPolicy</c>), así que se invalidan los
    /// trámites de la compañía titular del baúl.
    /// </summary>
    Task<int> InvalidarPorFirmaBaulAsync(Guid tenantId, CancellationToken cancellationToken = default);
}
