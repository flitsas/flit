namespace Flit.Admin.Domain.OtProfile;

public interface IOtProfileRepository
{
    Task<OtProfile?> GetByTenantAsync(Guid tenantId, CancellationToken cancellationToken = default);

    Task<OtProfile> SaveAsync(
        Guid tenantId,
        string operationMode,
        bool quipuxReadOnly,
        Guid? changedBy,
        Guid? transitOfficeId = null,
        CancellationToken cancellationToken = default);
}
