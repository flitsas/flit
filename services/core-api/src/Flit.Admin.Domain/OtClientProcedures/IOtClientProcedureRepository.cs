using Flit.Admin.Domain.Common;
using Flit.Queries.Domain;

namespace Flit.Admin.Domain.OtClientProcedures;

/// <summary>
/// Acceso cross-tenant a trámites de clientes con grant vigente hacia el OT (HU #10217).
/// </summary>
public interface IOtClientProcedureRepository
{
    Task<PagedResult<OtClientProcedure>> ListAsync(
        Guid otTenantId,
        OtClientProcedureFilter filter,
        Guid? transitOfficeIdOverride = null,
        CancellationToken cancellationToken = default);

    Task<OtClientProcedure?> GetByIdAsync(
        Guid otTenantId,
        Guid procedureInstanceId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Diagnóstico de la bandeja (HU #10540 / R09): cuenta los trámites <c>entregado</c> dirigidos
    /// al organismo del OT (o el <paramref name="transitOfficeIdOverride"/> del SuperAdmin),
    /// separando los que tienen grant vigente con la empresa cliente de los que no. Devuelve
    /// <c>null</c> cuando el tenant no resuelve ningún organismo de tránsito.
    /// </summary>
    Task<OtBandejaHealth?> GetDeliveryHealthAsync(
        Guid otTenantId,
        Guid? transitOfficeIdOverride = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Contadores de la cabecera de la bandeja (ver <see cref="OtBandejaCounters"/>): cuánto trabajo
    /// hay de cada clase en el organismo. Cuenta en SQL sobre el universo accesible —el mismo
    /// alcance de <see cref="ListAsync"/>, con grant vigente— y no sobre una página, porque la
    /// bandeja está paginada y contar la página respondería otra pregunta.
    /// <para>Devuelve <c>null</c> cuando el tenant no resuelve ningún organismo de tránsito.</para>
    /// </summary>
    /// <summary>
    /// El catálogo de campos filtrables de la bandeja (HU #12217), con las opciones que dependen del
    /// organismo ya resueltas: las empresas que le entregan de verdad y los tipos que de verdad ha
    /// recibido.
    ///
    /// <para>Se resuelven aquí y no en el catálogo estático porque ofrecer una empresa con la que
    /// este organismo nunca ha tramitado es ofrecer un filtro que solo puede devolver cero, y un
    /// filtro que devuelve cero se lee como que el dato no existe.</para>
    ///
    /// <para><c>null</c> cuando quien pregunta no tiene organismo resoluble, igual que el resto de
    /// la superficie OT.</para>
    /// </summary>
    Task<IReadOnlyList<QueryFieldDto>?> GetBandejaFilterFieldsAsync(
        Guid otTenantId,
        Guid? transitOfficeIdOverride = null,
        CancellationToken cancellationToken = default);

    Task<OtBandejaCounters?> GetBandejaCountersAsync(
        Guid otTenantId,
        Guid? transitOfficeIdOverride = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Variante con override de organismo para SuperAdmin (mismo contrato que
    /// <see cref="ListAsync"/>): si <paramref name="transitOfficeIdOverride"/> viene, el acceso
    /// se resuelve contra esa oficina del catálogo en lugar del perfil OT del tenant.
    /// </summary>
    Task<OtClientProcedure?> GetByIdAsync(
        Guid otTenantId,
        Guid procedureInstanceId,
        Guid? transitOfficeIdOverride,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Ejecuta <paramref name="action"/> dentro del scope RLS del tenant CLIENTE
    /// (<c>app.current_tenant_id</c> en la transacción), igual que approve/reject. Permite
    /// componer, desde el API, casos de uso del módulo Trámites (consolidado, adjuntos LT)
    /// sobre el trámite de un cliente cuyo acceso ya fue validado con <see cref="GetByIdAsync(Guid,Guid,Guid?,CancellationToken)"/>.
    /// </summary>
    Task<T> ExecuteInClientTenantScopeAsync<T>(
        Guid clientTenantId,
        Func<Task<T>> action,
        CancellationToken cancellationToken = default);

    Task<OtClientProcedure?> ApproveAsync(
        Guid otTenantId,
        Guid procedureInstanceId,
        Guid? approvedBy,
        string source,
        Guid? mandateSignerId = null,
        Guid? transitOfficeIdOverride = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Rechazo definitivo. <paramref name="rejectionReasonIds"/> son causales del catálogo global ya
    /// validadas por el handler; se persisten colgando del evento de rechazo (la fila de
    /// <c>procedure_instance_status_history</c>) para que el reporte de motivos pueda agregarlas.
    /// </summary>
    Task<OtClientProcedure?> RejectAsync(
        Guid otTenantId,
        Guid procedureInstanceId,
        string reason,
        Guid? rejectedBy,
        string source,
        Guid? transitOfficeIdOverride = null,
        IReadOnlyList<Guid>? rejectionReasonIds = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Observación subsanable del OT: transiciona a <c>rechazado</c> con checklist HÍBRIDO
    /// (motivo + ítems) en metadata. El operador activa la edición con POST /subsanar.
    /// </summary>
    Task<OtClientProcedure?> ObserveAsync(
        Guid otTenantId,
        Guid procedureInstanceId,
        string reason,
        IReadOnlyList<OtProcedureObservationItem> items,
        Guid? observedBy,
        string source,
        Guid? transitOfficeIdOverride = null,
        IReadOnlyList<Guid>? rejectionReasonIds = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// HU #10654 / #10800 (Feature #10587) — el OT asigna una placa a un trámite en <c>preasignado</c>
    /// (Flujo B): reserva la placa (del rango, o FUERA DE RANGO si <paramref name="outOfRange"/> — la
    /// registra como rango ad-hoc de 1 placa), la escribe en el trámite y avanza el sub-estado a
    /// <c>asignado</c>. Si no se puede, el resultado trae la causa concreta en
    /// <see cref="PlateAssignmentFailure"/> — en particular distingue la placa YA asignada, que es el
    /// error habitual en operación y antes llegaba al usuario como un mensaje genérico.
    /// </summary>
    Task<PlateAssignmentOutcome> AssignPlateAsync(
        Guid otTenantId,
        Guid procedureInstanceId,
        string plate,
        Guid? changedBy,
        string source,
        bool outOfRange = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// HU #10655 (Feature #10587) — el OT revoca la preasignación: libera la placa
    /// (preasignada→revocada) y devuelve el trámite a <c>preasignado</c> si estaba <c>asignado</c>.
    /// </summary>
    Task<OtClientProcedure?> RevokePlateAsync(
        Guid otTenantId,
        Guid procedureInstanceId,
        string reason,
        Guid? changedBy,
        string source,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// HU #12167 (Feature #12156) — el OT corrige la placa asignada dentro de la hora siguiente a
    /// <c>plate_assigned_at</c> (una única oportunidad, <c>plate_updated_at</c> nulo). Reutiliza el
    /// mismo mecanismo de escritura de <see cref="AssignPlateAsync"/> (field_value <c>plate</c>,
    /// denormalizado por trigger) y dispara <see cref="PlateAssignmentFailure.PlateUpdateWindowExpired"/>
    /// o <see cref="PlateAssignmentFailure.PlateUpdateAlreadyUsed"/> según cuál de las dos condiciones
    /// falle. Registra un <c>ProcedureInstanceEvent</c> (placa anterior/nueva/usuario/fecha) — no hay
    /// transición de estado, así que no aplica <c>procedure_instance_status_history</c>.
    /// </summary>
    Task<PlateAssignmentOutcome> UpdatePlateAsync(
        Guid otTenantId,
        Guid procedureInstanceId,
        string plate,
        Guid? changedBy,
        string source,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// HU #12166 (Feature #12156) — el OT deshace su propia aprobación: <c>aprobado → revocado</c>
    /// (única salida de <c>aprobado</c> en <see cref="Domain.Tramites.Estados.TramiteStateMachine"/>,
    /// alcanzable SOLO por este método). En la misma transacción: libera la placa (Revocado entra en
    /// <c>EstadosQueLiberanPlaca</c>, así que no hace falta tocar <c>plate_range_details</c> aparte) y
    /// marca el FUR/certificados vigentes como históricos (<c>ProcedureInstanceAttachment.IsHistorico</c>).
    /// Devuelve <c>null</c> si el trámite no es accesible o no está en <c>aprobado</c> (409
    /// <c>INVALID_STATE</c>, mismo patrón que <see cref="ApproveAsync"/>/<see cref="RejectAsync"/>).
    /// </summary>
    Task<OtClientProcedure?> RevokeAsync(
        Guid otTenantId,
        Guid procedureInstanceId,
        string? reason,
        Guid? changedBy,
        string source,
        Guid? transitOfficeIdOverride = null,
        CancellationToken cancellationToken = default);
}
