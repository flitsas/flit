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
}
