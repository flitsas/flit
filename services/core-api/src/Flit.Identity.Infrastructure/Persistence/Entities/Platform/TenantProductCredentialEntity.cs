namespace Flit.Infrastructure.Persistence.Entities.Platform;

/// <summary>
/// Credencial de producto por empresa — <c>platform.tenant_product_credentials</c> (ADR-0092).
/// Solo core-identity lee y escribe. Tenant-scoped: el aislamiento es el <c>WHERE tenant_id</c> del repositorio.
/// </summary>
public sealed class TenantProductCredentialEntity
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    public string ProductCode { get; set; } = string.Empty;

    public string CredentialCode { get; set; } = string.Empty;

    public string SecretProtected { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }

    public long RowVersion { get; set; }
}
