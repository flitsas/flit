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

    /// <summary>
    /// HU #14694 (D-CRED-DIAG) — deja en <c>admin.tenant_config_audit_logs</c> que <paramref name="lector"/> leyó la
    /// credencial para copiarla (sin valor ni huella). Se llama también si no hay fila (<paramref name="existe"/> en false).
    /// </summary>
    Task RegistrarLecturaParaCopiaAsync(
        Guid tenantId,
        string productCode,
        string credentialCode,
        string lector,
        bool existe,
        CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(
        Guid tenantId,
        string productCode,
        string credentialCode,
        Guid? changedBy,
        CancellationToken cancellationToken = default);
}
