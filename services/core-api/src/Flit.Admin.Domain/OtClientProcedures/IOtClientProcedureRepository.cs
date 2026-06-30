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
