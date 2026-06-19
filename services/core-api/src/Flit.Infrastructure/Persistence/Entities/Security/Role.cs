namespace Flit.Infrastructure.Persistence.Entities.Security;

public sealed class Role : Entities.Common.TenantAuditableEntity
{
    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public bool IsSystem { get; set; }
}
