using Flit.Admin.Domain.Common;

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
}
