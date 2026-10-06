namespace Flit.Infrastructure.Persistence.Entities.Common;

public abstract class TenantAuditableEntity : AuditableEntity
{
    public Guid TenantId { get; set; }
}
