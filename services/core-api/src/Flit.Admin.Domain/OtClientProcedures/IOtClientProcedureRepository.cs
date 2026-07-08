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
        CancellationToken cancellationToken = default);

    Task<OtClientProcedure?> RejectAsync(
        Guid otTenantId,
        Guid procedureInstanceId,
        string reason,
        Guid? rejectedBy,
        string source,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// HU #10654 (Feature #10587) — el OT asigna una placa a un trámite en <c>preasignado</c> (Flujo B):
    /// reserva la placa, la escribe en el trámite y lo transiciona a <c>asignado</c>, devolviéndolo a la
    /// compañía. Devuelve <c>null</c> si el trámite no es accesible, no está en preasignado o la placa no
    /// está disponible.
    /// </summary>
    Task<OtClientProcedure?> AssignPlateAsync(
        Guid otTenantId,
        Guid procedureInstanceId,
        string plate,
        Guid? changedBy,
        string source,
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
