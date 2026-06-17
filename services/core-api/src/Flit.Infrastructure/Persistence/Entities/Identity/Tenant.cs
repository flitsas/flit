namespace Flit.Infrastructure.Persistence.Entities.Identity;

public sealed class Tenant
{
    public Guid Id { get; set; }

    public string Code { get; set; } = string.Empty;

    public string LegalName { get; set; } = string.Empty;

    public string TaxId { get; set; } = string.Empty;

    public string TenantType { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }

    public long RowVersion { get; set; }
}
