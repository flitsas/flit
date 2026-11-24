namespace Flit.Modules.Platform.Domain.TenantProducts;

/// <summary>
/// Persistencia de <c>platform.tenant_product_credentials</c>. La auditoría legible va a
/// <c>admin.tenant_config_audit_logs</c> sin valor ni huella (ADR-0092).
/// </summary>
public interface ITenantProductCredentialRepository
{
    Task<TenantProductCredential?> GetAsync(Guid tenantId, string productCode, string credentialCode, CancellationToken cancellationToken = default);

    Task<bool> ExistsAsync(Guid tenantId, string productCode, string credentialCode, CancellationToken cancellationToken = default);

    Task<TenantProductCredentialWrite> SetAsync(
        Guid tenantId,
        string productCode,
        string credentialCode,
        string secretProtected,
        Guid? changedBy,
        CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(
        Guid tenantId,
        string productCode,
        string credentialCode,
        Guid? changedBy,
        CancellationToken cancellationToken = default);
}
